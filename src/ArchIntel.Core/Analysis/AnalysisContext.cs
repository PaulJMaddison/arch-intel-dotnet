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
        ILogger logger)
    {
        SolutionPath = Path.GetFullPath(solutionPath);
        RepoRootPath = Path.GetFullPath(repoRootPath);
        Solution = solution;
        Config = config;
        Logger = logger;

        OutputDir = Paths.GetReportsDirectory(RepoRootPath, config.OutputDir);
        CacheDir = Paths.GetCacheDirectory(RepoRootPath, config.CacheDir);
        MaxDegreeOfParallelism = config.GetEffectiveMaxDegreeOfParallelism();
        AnalysisVersion = typeof(AnalysisContext).Assembly.GetName().Version?.ToString() ?? "unknown";
    }

    public string SolutionPath { get; }
    public string RepoRootPath { get; }
    public string OutputDir { get; }
    public string CacheDir { get; }
    public Solution Solution { get; }
    public string AnalysisVersion { get; }
    public ILogger Logger { get; }
    public AnalysisConfig Config { get; }
    public int MaxDegreeOfParallelism { get; }
}
