using ArchIntel.Logging;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace ArchIntel.Analysis;

public sealed class SolutionLoader : ISolutionLoader
{
    private readonly ILogger _logger;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public SolutionLoader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<SolutionLoadResult> LoadAsync(
        string solutionPathOrDirectory,
        bool failOnLoadIssues,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var solutionPath = ResolveSolutionPath(solutionPathOrDirectory);
        var repoRootPath = ResolveRepoRootPath(solutionPathOrDirectory, solutionPath);

        SafeLog.Info(_logger, "Loading solution {Solution}.", SafeLog.SanitizePath(solutionPath));

        EnsureMsBuildRegistered();

        using var workspace = MSBuildWorkspace.Create();
        var diagnostics = new List<WorkspaceDiagnostic>();
        workspace.WorkspaceFailed += (_, args) =>
        {
            diagnostics.Add(args.Diagnostic);
            if (verbose)
            {
                SafeLog.Warn(
                    _logger,
                    "MSBuild workspace {Kind}: {Message}",
                    args.Diagnostic.Kind,
                    SafeLog.SanitizeValue(args.Diagnostic.Message));
            }
        };

        Solution solution;
        try
        {
            solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new SolutionLoadException(
                "Failed to load the solution with MSBuild. Ensure the .NET SDK is installed and the solution builds locally.",
                ex);
        }

        return BuildLoadResult(solutionPath, repoRootPath, solution, diagnostics);
    }

    internal static string ResolveSolutionPath(string solutionPathOrDirectory)
    {
        if (string.IsNullOrWhiteSpace(solutionPathOrDirectory))
        {
            throw new SolutionLoadException("Solution path is required. Provide a .sln file or a directory containing one.");
        }

        var fullPath = Path.GetFullPath(solutionPathOrDirectory);

        if (File.Exists(fullPath))
        {
            if (!string.Equals(Path.GetExtension(fullPath), ".sln", StringComparison.OrdinalIgnoreCase))
            {
                throw new SolutionLoadException("The provided file is not a .sln file. Provide a solution file or directory.");
            }

            return fullPath;
        }

        if (!Directory.Exists(fullPath))
        {
            throw new SolutionLoadException("The provided path does not exist. Provide a valid .sln file or directory.");
        }

        var solutions = Directory.EnumerateFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly)
            .Select(path => new SolutionCandidate(path, CountProjects(path)))
            .ToList();

        if (solutions.Count == 0)
        {
            throw new SolutionLoadException("No .sln files were found in the provided directory.");
        }

        return solutions
            .OrderByDescending(candidate => candidate.ProjectCount)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .First()
            .Path;
    }

    internal static string ResolveRepoRootPath(string solutionPath)
    {
        return ResolveRepoRootPath(solutionPath, solutionPath);
    }

    internal static string ResolveRepoRootPath(string solutionPathOrDirectory, string solutionPath)
    {
        var inputPath = Path.GetFullPath(solutionPathOrDirectory);
        if (Directory.Exists(inputPath))
        {
            return inputPath;
        }

        var directoryPath = Path.GetDirectoryName(solutionPath);
        return string.IsNullOrWhiteSpace(directoryPath)
            ? Directory.GetCurrentDirectory()
            : directoryPath;
    }

    private static int CountProjects(string solutionPath)
    {
        try
        {
            var lines = File.ReadLines(solutionPath);
            return lines.Count(line => line.TrimStart().StartsWith("Project(", StringComparison.Ordinal));
        }
        catch
        {
            return 0;
        }
    }

    private void EnsureMsBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered)
        {
            return;
        }

        var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
        if (instances.Length == 0)
        {
            throw new SolutionLoadException(
                "MSBuild SDKs were not found. Install the .NET SDK or Visual Studio Build Tools to load solutions.");
        }

        var instance = instances.OrderByDescending(candidate => candidate.Version).First();
        try
        {
            MSBuildLocator.RegisterInstance(instance);
        }
        catch (Exception ex)
        {
            throw new SolutionLoadException(
                "MSBuild could not be registered. Verify the .NET SDK installation.",
                ex);
        }

        SafeLog.Info(
            _logger,
            "Using MSBuild {Version} at {Path}.",
            instance.Version,
            instance.MSBuildPath);
    }

    internal static SolutionLoadResult BuildLoadResult(
        string solutionPath,
        string repoRootPath,
        Solution solution,
        IReadOnlyList<WorkspaceDiagnostic> diagnostics)
    {
        var projectCount = solution.Projects.Count();
        var failedProjectCount = CalculateFailedProjectCount(solution, diagnostics);

        var loadDiagnostics = diagnostics
            .Select(diagnostic =>
            {
                var isFatal = IsFatalWorkspaceDiagnostic(diagnostic);
                var message = SanitizeDiagnosticMessage(diagnostic.Message);
                return new LoadDiagnostic(diagnostic.Kind.ToString(), message, isFatal);
            })
            .OrderBy(diagnostic => diagnostic.Kind, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.IsFatal)
            .ToList()
            .AsReadOnly();

        return new SolutionLoadResult(solutionPath, repoRootPath, solution, loadDiagnostics, projectCount, failedProjectCount);
    }

    internal static bool IsFatalWorkspaceDiagnostic(WorkspaceDiagnostic diagnostic)
    {
        var message = diagnostic.Message ?? string.Empty;
        var normalized = message.Trim();

        if (ContainsAny(normalized, "depends on", "was not found", "was resolved", "nuget")
            || System.Text.RegularExpressions.Regex.IsMatch(normalized, "\\bNU\\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (normalized.Contains("The SDK 'Microsoft.NET.Sdk' specified could not be found", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("MSBuild SDKs were not found", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("could not be found", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("sdk", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("Unable to load project file", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains("error MSB", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(normalized, "\\bMSB\\d+", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static string SanitizeDiagnosticMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var sanitized = message.ReplaceLineEndings(" ").Trim();
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            "[A-Za-z]:\\\\Users\\\\[^\\\\/\\s]+",
            "<userdir>");
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            "/(Users|home)/[^/\\s]+",
            "<userdir>");
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            "(^|[\\s\"'\\(=])([A-Za-z]:\\\\[^\\s\"']+)",
            match => $"{match.Groups[1].Value}<path>");
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            "(^|[\\s\"'\\(=])(/[^\\s\"']+)",
            match => $"{match.Groups[1].Value}<path>");
        return sanitized;
    }

    private static int CalculateFailedProjectCount(Solution solution, IReadOnlyList<WorkspaceDiagnostic> diagnostics)
    {
        var projectPaths = solution.Projects
            .Select(project => project.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .ToHashSet(PathComparer);

        var fileNameLookup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in projectPaths)
        {
            var fileName = Path.GetFileName(path);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (!fileNameLookup.TryGetValue(fileName, out var list))
            {
                list = new List<string>();
                fileNameLookup[fileName] = list;
            }

            list.Add(path);
        }

        var failedProjects = new HashSet<string>(PathComparer);
        var fatalUnmapped = 0;

        foreach (var diagnostic in diagnostics)
        {
            if (!IsFatalWorkspaceDiagnostic(diagnostic))
            {
                continue;
            }

            var mapped = false;
            foreach (var candidate in ExtractProjectPaths(diagnostic.Message))
            {
                var normalized = candidate;
                if (Path.IsPathRooted(candidate))
                {
                    normalized = Path.GetFullPath(candidate);
                }

                if (projectPaths.Contains(normalized))
                {
                    failedProjects.Add(normalized);
                    mapped = true;
                    continue;
                }

                var fileName = Path.GetFileName(candidate);
                if (!string.IsNullOrWhiteSpace(fileName)
                    && fileNameLookup.TryGetValue(fileName, out var matches)
                    && matches.Count == 1)
                {
                    failedProjects.Add(matches[0]);
                    mapped = true;
                }
            }

            if (!mapped)
            {
                fatalUnmapped += 1;
            }
        }

        return failedProjects.Count + fatalUnmapped;
    }

    private static IEnumerable<string> ExtractProjectPaths(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            yield break;
        }

        var candidates = System.Text.RegularExpressions.Regex.Matches(
            message,
            "([A-Za-z]:\\\\[^\\s\"']+\\.(csproj|vbproj|fsproj))|(/[^\\s\"']+\\.(csproj|vbproj|fsproj))|(\\b[^\\s\"']+\\.(csproj|vbproj|fsproj))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (System.Text.RegularExpressions.Match match in candidates)
        {
            if (!match.Success)
            {
                continue;
            }

            yield return match.Value;
        }
    }

    private sealed record SolutionCandidate(string Path, int ProjectCount);
}

public sealed class SolutionLoadException : Exception
{
    public SolutionLoadException(string message) : base(message)
    {
    }

    public SolutionLoadException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
