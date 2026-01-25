using ArchIntel.Configuration;
using ArchIntel.IO;

namespace ArchIntel.Analysis;

public sealed class AnalysisContext
{
    public AnalysisContext(string solutionPath, AnalysisConfig config)
    {
        SolutionPath = Path.GetFullPath(solutionPath);
        Config = config;

        var baseDirectory = Directory.GetCurrentDirectory();
        OutputDirectory = Paths.GetReportsDirectory(baseDirectory, config.OutputDir);
        CacheDirectory = Paths.GetCacheDirectory(baseDirectory, config.CacheDir);
        MaxDegreeOfParallelism = config.GetEffectiveMaxDegreeOfParallelism();
    }

    public string SolutionPath { get; }
    public AnalysisConfig Config { get; }
    public string OutputDirectory { get; }
    public string CacheDirectory { get; }
    public int MaxDegreeOfParallelism { get; }
}
