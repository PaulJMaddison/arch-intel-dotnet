using ArchIntel.Logging;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace ArchIntel.Analysis;

public sealed class SolutionLoader : ISolutionLoader
{
    private readonly ILogger _logger;

    public SolutionLoader(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<SolutionLoadResult> LoadAsync(
        string solutionPathOrDirectory,
        bool failOnLoadIssues,
        CancellationToken cancellationToken)
    {
        var solutionPath = ResolveSolutionPath(solutionPathOrDirectory);
        var repoRootPath = ResolveRepoRootPath(solutionPath);

        SafeLog.Info(_logger, "Loading solution {Solution}.", SafeLog.SanitizePath(solutionPath));

        EnsureMsBuildRegistered();

        using var workspace = MSBuildWorkspace.Create();
        var diagnostics = new List<WorkspaceDiagnostic>();
        workspace.WorkspaceFailed += (_, args) =>
        {
            diagnostics.Add(args.Diagnostic);
            SafeLog.Warn(
                _logger,
                "MSBuild workspace {Kind}: {Message}",
                args.Diagnostic.Kind,
                SafeLog.SanitizeValue(args.Diagnostic.Message));
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

        return BuildLoadResult(solutionPath, repoRootPath, solution, diagnostics, failOnLoadIssues);
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
        var directoryPath = Path.GetDirectoryName(solutionPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return Directory.GetCurrentDirectory();
        }

        var current = new DirectoryInfo(directoryPath);
        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return directoryPath;
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

    private static void EnsureMsBuildRegistered()
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
    }

    internal static SolutionLoadResult BuildLoadResult(
        string solutionPath,
        string repoRootPath,
        Solution solution,
        IReadOnlyList<WorkspaceDiagnostic> diagnostics,
        bool failOnLoadIssues)
    {
        if (!solution.Projects.Any())
        {
            throw new SolutionLoadException("MSBuild loaded the solution, but it contains no projects.");
        }

        var loadDiagnostics = diagnostics
            .Select(diagnostic =>
            {
                var isFatal = IsFatalWorkspaceDiagnostic(diagnostic);
                return new LoadDiagnostic(diagnostic.Kind.ToString(), diagnostic.Message, isFatal);
            })
            .ToList()
            .AsReadOnly();

        if (failOnLoadIssues)
        {
            var fatal = loadDiagnostics.FirstOrDefault(diagnostic => diagnostic.IsFatal);
            if (fatal is not null)
            {
                throw new SolutionLoadException(
                    $"MSBuild reported a fatal load issue: {SafeLog.SanitizeValue(fatal.Message)}");
            }
        }

        return new SolutionLoadResult(solutionPath, repoRootPath, solution, loadDiagnostics);
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
