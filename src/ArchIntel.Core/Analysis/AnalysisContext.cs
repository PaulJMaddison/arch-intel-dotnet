using System.Reflection;
using ArchIntel.Configuration;
using ArchIntel.IO;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace ArchIntel.Analysis;

public sealed class AnalysisContext
{
    public AnalysisContext(
        string solutionPath,
        string repoRootPath,
        Solution solution,
        AnalysisConfig config,
        ILogger logger,
        int projectCount,
        int failedProjectCount,
        PipelineTimer? pipelineTimer = null,
        IReadOnlyList<LoadDiagnostic>? loadDiagnostics = null,
        string? cliInvocation = null)
    {
        SolutionPath = Path.GetFullPath(solutionPath);
        RepoRootPath = Path.GetFullPath(repoRootPath);
        Solution = solution;
        Config = config;
        Logger = logger;
        ProjectCount = projectCount;
        FailedProjectCount = failedProjectCount;
        PipelineTimer = pipelineTimer;
        LoadDiagnostics = loadDiagnostics ?? Array.Empty<LoadDiagnostic>();

        OutputDir = Paths.GetReportsDirectory(RepoRootPath, config.OutputDir);
        CacheDir = Paths.GetCacheDirectory(RepoRootPath, config.CacheDir);
        MaxDegreeOfParallelism = config.GetEffectiveMaxDegreeOfParallelism();
        AnalysisVersion = ResolveAnalysisVersion();
        CliInvocation = cliInvocation;
    }

    public string SolutionPath { get; }
    public string RepoRootPath { get; }
    public string OutputDir { get; }
    public string CacheDir { get; }
    public Solution Solution { get; }
    public int ProjectCount { get; }
    public int FailedProjectCount { get; }
    public string AnalysisVersion { get; }
    public ILogger Logger { get; }
    public AnalysisConfig Config { get; }
    public int MaxDegreeOfParallelism { get; }
    public PipelineTimer? PipelineTimer { get; }
    public IReadOnlyList<LoadDiagnostic> LoadDiagnostics { get; }
    public string? CliInvocation { get; }

    private static string ResolveAnalysisVersion()
    {
        var assembly = typeof(AnalysisContext).Assembly;
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(info?.InformationalVersion))
        {
            return info.InformationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
