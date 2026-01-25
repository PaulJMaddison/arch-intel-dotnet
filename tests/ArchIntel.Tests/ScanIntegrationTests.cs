using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.Reports;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ScanIntegrationTests
{
    [SkippableFact]
    public async Task ScanReport_IsDeterministic()
    {
        Skip.IfNot(
            MSBuildLocator.QueryVisualStudioInstances().Any(),
            "MSBuild SDKs were not found; skipping integration test.");
        using var temp = new TemporaryDirectory();
        var solutionPath = GetTestSolutionPath();
        var loader = new SolutionLoader(NullLogger.Instance);
        var loadResult = await loader.LoadAsync(solutionPath, false, false, CancellationToken.None);
        Assert.True(loadResult.ProjectCount > 0);

        var outputDir = Path.Combine(temp.Path, "output");
        var cacheDir = Path.Combine(temp.Path, "cache");

        var first = await RunScanAsync(loadResult.Solution, loadResult.RepoRootPath, loadResult.LoadDiagnostics, outputDir, cacheDir);
        if (Directory.Exists(cacheDir))
        {
            Directory.Delete(cacheDir, true);
        }

        var second = await RunScanAsync(loadResult.Solution, loadResult.RepoRootPath, loadResult.LoadDiagnostics, outputDir, cacheDir);

        Assert.Equal(first.Count, second.Count);
        foreach (var entry in first)
        {
            Assert.True(second.ContainsKey(entry.Key));
            Assert.Equal(entry.Value, second[entry.Key]);
        }
    }

    [Fact]
    public async Task ScanReport_DoesNotThrow_WhenOnlyNonFatalLoadDiagnostics()
    {
        using var temp = new TemporaryDirectory();
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution.AddProject("Test", "Test", LanguageNames.CSharp).Solution;
        var outputDir = Path.Combine(temp.Path, "output");
        var cacheDir = Path.Combine(temp.Path, "cache");

        var context = new AnalysisContext(
            Path.Combine(temp.Path, "test.sln"),
            temp.Path,
            solution,
            new AnalysisConfig
            {
                OutputDir = outputDir,
                CacheDir = cacheDir,
                MaxDegreeOfParallelism = 1
            },
            NullLogger.Instance,
            solution.Projects.Count(),
            0,
            loadDiagnostics: new[]
            {
                new LoadDiagnostic("Failure", "NU1605 Detected package downgrade.", false)
            });

        var writer = new ReportWriter();
        _ = await writer.WriteAsync(context, "scan", null, ReportFormat.Json, CancellationToken.None);

        var summaryPath = Path.Combine(outputDir, "scan_summary.json");
        var summaryJson = File.ReadAllText(summaryPath);
        Assert.Contains("loadDiagnostics", summaryJson, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, string>> RunScanAsync(
        Solution solution,
        string repoRoot,
        IReadOnlyList<LoadDiagnostic> loadDiagnostics,
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
            NullLogger.Instance,
            solution.Projects.Count(),
            loadDiagnostics.Count(diagnostic => diagnostic.IsFatal),
            loadDiagnostics: loadDiagnostics);

        var writer = new ReportWriter();
        _ = await writer.WriteAsync(context, "scan", null, ReportFormat.Json, CancellationToken.None);

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
            "namespaces.json",
            "README.md"
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
