using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public static class DocumentationCaptureReport
{
    private const int MaxHeadingCount = 8;
    private const int SnippetCharacterLimit = 2000;
    private static readonly Regex HeadingRegex = new(@"^\s{0,3}#{1,6}\s+(?<text>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}_]+\b", RegexOptions.Compiled);

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var includeSnippets = context.Config.IncludeDocSnippets;
        var data = await CreateAsync(context, fileSystem, includeSnippets, cancellationToken);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        var outputPath = Path.Combine(outputDirectory, "docs.json");
        await fileSystem.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    internal static async Task<DocumentationCaptureData> CreateAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        bool includeSnippets,
        CancellationToken cancellationToken)
    {
        var repoRoot = context.RepoRootPath;
        var documents = new List<DocumentationFileEntry>();
        var hashService = new DocumentHashService(fileSystem);

        foreach (var filePath in DiscoverDocumentationFiles(repoRoot))
        {
            var entry = await BuildEntryAsync(repoRoot, filePath, includeSnippets, fileSystem, hashService, cancellationToken);
            documents.Add(entry);
        }

        var directoryBuildProps = File.Exists(Path.Combine(repoRoot, "Directory.Build.props"));
        var directoryBuildTargets = File.Exists(Path.Combine(repoRoot, "Directory.Build.targets"));

        var topLevelDirectories = Directory.EnumerateDirectories(repoRoot)
            .Select(path => Path.GetFileName(path))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Where(name => !name.Equals(".git", StringComparison.OrdinalIgnoreCase))
            .Where(name => !name.Equals(Paths.ToolDirectoryName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        return new DocumentationCaptureData(
            documents,
            new DocumentationCaptureSummary(documents.Count, includeSnippets),
            new BuildFilePresence(directoryBuildProps, directoryBuildTargets),
            new TopLevelSummary(topLevelDirectories));
    }

    internal static IReadOnlyList<string> ExtractHeadings(string content, int maxHeadings)
    {
        var headings = new List<string>();
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var match = HeadingRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var text = match.Groups["text"].Value.Trim();
            text = Regex.Replace(text, @"\s+#+\s*$", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            headings.Add(text);
            if (headings.Count >= maxHeadings)
            {
                break;
            }
        }

        return headings;
    }

    private static async Task<DocumentationFileEntry> BuildEntryAsync(
        string repoRoot,
        string filePath,
        bool includeSnippets,
        IFileSystem fileSystem,
        DocumentHashService hashService,
        CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextAsync(filePath, cancellationToken);
        var normalizedContent = NormalizeLineEndings(content);
        var headings = ExtractHeadings(normalizedContent, MaxHeadingCount);
        var title = headings.Count > 0 ? headings[0] : Path.GetFileNameWithoutExtension(filePath);
        var wordCount = WordRegex.Matches(content).Count;
        var snippet = includeSnippets
            ? content[..Math.Min(SnippetCharacterLimit, content.Length)]
            : null;
        var relativePath = NormalizePath(Path.GetRelativePath(repoRoot, filePath));
        var contentHash = hashService.GetContentHash(normalizedContent);

        return new DocumentationFileEntry(relativePath, title, wordCount, contentHash, snippet);
    }

    private static IReadOnlyList<string> DiscoverDocumentationFiles(string repoRoot)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readmePath = Path.Combine(repoRoot, "README.md");
        if (File.Exists(readmePath))
        {
            candidates.Add(Path.GetFullPath(readmePath));
        }

        var changelogPath = Path.Combine(repoRoot, "CHANGELOG.md");
        if (File.Exists(changelogPath))
        {
            candidates.Add(Path.GetFullPath(changelogPath));
        }

        var docsDirectory = Path.Combine(repoRoot, "docs");
        if (Directory.Exists(docsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(docsDirectory, "*.md", SearchOption.AllDirectories))
            {
                candidates.Add(Path.GetFullPath(file));
            }

            var adrDirectory = Path.Combine(docsDirectory, "adr");
            if (Directory.Exists(adrDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(adrDirectory, "*", SearchOption.AllDirectories))
                {
                    candidates.Add(Path.GetFullPath(file));
                }
            }
        }

        foreach (var markdownFile in Directory.EnumerateFiles(repoRoot, "*.md", SearchOption.AllDirectories))
        {
            if (IsArchitectureConventionFile(markdownFile))
            {
                candidates.Add(Path.GetFullPath(markdownFile));
            }
        }

        return candidates
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizePath(Path.GetRelativePath(repoRoot, path))
            })
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .Select(entry => entry.FullPath)
            .ToArray();
    }

    private static bool IsArchitectureConventionFile(string markdownPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(markdownPath);
        return fileName.Contains("architecture", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("architectural", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("design", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("adr", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("decision", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed record DocumentationCaptureData(
    IReadOnlyList<DocumentationFileEntry> Documents,
    DocumentationCaptureSummary Summary,
    BuildFilePresence BuildFiles,
    TopLevelSummary TopLevel);

public sealed record DocumentationFileEntry(
    string RelativePath,
    string Title,
    int WordCount,
    string ContentHash,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Snippet);

public sealed record DocumentationCaptureSummary(int AnalyzedDocuments, bool IncludeSnippets);

public sealed record BuildFilePresence(bool DirectoryBuildProps, bool DirectoryBuildTargets);

public sealed record TopLevelSummary(IReadOnlyList<string> Directories);
