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

        var readmePath = FindReadme(repoRoot);
        if (readmePath is not null)
        {
            var entry = await BuildEntryAsync(repoRoot, readmePath, includeSnippets, fileSystem, cancellationToken);
            documents.Add(entry);
        }

        var docsDirectory = Path.Combine(repoRoot, "docs");
        if (Directory.Exists(docsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(docsDirectory, "*.md", SearchOption.TopDirectoryOnly)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var entry = await BuildEntryAsync(repoRoot, file, includeSnippets, fileSystem, cancellationToken);
                documents.Add(entry);
            }
        }

        documents.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));

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
            new BuildFilePresence(directoryBuildProps, directoryBuildTargets),
            new TopLevelSummary(topLevelDirectories),
            includeSnippets);
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
        CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextAsync(filePath, cancellationToken);
        var headings = ExtractHeadings(content, MaxHeadingCount);
        var wordCount = WordRegex.Matches(content).Count;
        var snippet = includeSnippets
            ? content[..Math.Min(SnippetCharacterLimit, content.Length)]
            : null;
        var relativePath = NormalizePath(Path.GetRelativePath(repoRoot, filePath));

        return new DocumentationFileEntry(relativePath, wordCount, headings, snippet);
    }

    private static string? FindReadme(string repoRoot)
    {
        var readmePath = Path.Combine(repoRoot, "README.md");
        if (File.Exists(readmePath))
        {
            return readmePath;
        }

        return Directory.EnumerateFiles(repoRoot, "README.*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

public sealed record DocumentationCaptureData(
    IReadOnlyList<DocumentationFileEntry> Documents,
    BuildFilePresence BuildFiles,
    TopLevelSummary TopLevel,
    bool IncludeSnippets);

public sealed record DocumentationFileEntry(
    string Path,
    int WordCount,
    IReadOnlyList<string> Headings,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Snippet);

public sealed record BuildFilePresence(bool DirectoryBuildProps, bool DirectoryBuildTargets);

public sealed record TopLevelSummary(IReadOnlyList<string> Directories);
