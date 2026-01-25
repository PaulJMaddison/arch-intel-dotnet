using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ScanIntegrationTests
{
    [SkippableFact]
    public async Task ScanReport_IsDeterministic()
    {
        Skip.IfNot(MsBuildAvailability.IsAvailable(), "MSBuild SDKs were not found; skipping integration test.");
        using var temp = new TemporaryDirectory();
        var solutionPath = GetTestSolutionPath();
        var loader = new SolutionLoader(NullLogger.Instance);
        var loadResult = await loader.LoadAsync(solutionPath, CancellationToken.None);

        var output1 = Path.Combine(temp.Path, "output1");
        var cache1 = Path.Combine(temp.Path, "cache1");
        var output2 = Path.Combine(temp.Path, "output2");
        var cache2 = Path.Combine(temp.Path, "cache2");

        var first = await RunScanAsync(loadResult.Solution, loadResult.RepoRootPath, output1, cache1);
        var second = await RunScanAsync(loadResult.Solution, loadResult.RepoRootPath, output2, cache2);

        Assert.Equal(first.Count, second.Count);
        foreach (var entry in first)
        {
            Assert.True(second.ContainsKey(entry.Key));
            Assert.Equal(entry.Value, second[entry.Key]);
        }
    }

    private static async Task<Dictionary<string, string>> RunScanAsync(
        Solution solution,
        string repoRoot,
        string outputDir,
        string cacheDir)
    {
        var config = new AnalysisConfig
        {
            OutputDir = outputDir,
            CacheDir = cacheDir,
            MaxDegreeOfParallelism = 2
        };

        var context = new AnalysisContext(
            Path.Combine(repoRoot, "arch.sln"),
            repoRoot,
            solution,
            config,
            NullLogger.Instance);

        var writer = new ReportWriter();
        await writer.WriteAsync(context, "scan", null, ReportFormat.Json, CancellationToken.None);

        return ReadOutputs(outputDir);
    }

    private static Dictionary<string, string> ReadOutputs(string outputDir)
    {
        var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
        var fileNames = new[]
        {
            "scan.json",
            "scan_summary.json",
            "symbols.json",
            "namespaces.json"
        };

        foreach (var fileName in fileNames)
        {
            var path = Path.Combine(outputDir, fileName);
            outputs[fileName] = File.ReadAllText(path);
        }

        return outputs;
    }

    private static string GetTestSolutionPath()
    {
        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        return Path.Combine(repoRoot, "tests", "TestData", "SampleSolution", "ArchIntel.TestData.sln");
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for test data.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "archintel-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
