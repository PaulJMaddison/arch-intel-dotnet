using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ScanIntegrationTests
{
    [Fact]
    public async Task ScanReport_IsDeterministic()
    {
        using var temp = new TemporaryDirectory();
        var solution = CreateSolution(temp.Path);

        var output1 = Path.Combine(temp.Path, "output1");
        var cache1 = Path.Combine(temp.Path, "cache1");
        var output2 = Path.Combine(temp.Path, "output2");
        var cache2 = Path.Combine(temp.Path, "cache2");

        var first = await RunScanAsync(solution, temp.Path, output1, cache1);
        var second = await RunScanAsync(solution, temp.Path, output2, cache2);

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

    private static Solution CreateSolution(string repoRoot)
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var appId = ProjectId.CreateNewId();
        var dataId = ProjectId.CreateNewId();

        var appPath = Path.Combine(repoRoot, "src", "App", "App.csproj");
        var dataPath = Path.Combine(repoRoot, "src", "Data", "Data.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(appPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);

        solution = solution.AddProject(ProjectInfo.Create(appId, VersionStamp.Create(), "App", "App", LanguageNames.CSharp, filePath: appPath));
        solution = solution.AddProject(ProjectInfo.Create(dataId, VersionStamp.Create(), "Data", "Data", LanguageNames.CSharp, filePath: dataPath));
        solution = solution.AddProjectReference(appId, new ProjectReference(dataId));

        solution = solution.AddDocument(
                DocumentId.CreateNewId(appId),
                "Program.cs",
                "namespace App; public class Program { }",
                filePath: Path.Combine(repoRoot, "src", "App", "Program.cs"))
            .Project.Solution;

        solution = solution.AddDocument(
                DocumentId.CreateNewId(dataId),
                "Repository.cs",
                "namespace Data; public class Repository { }",
                filePath: Path.Combine(repoRoot, "src", "Data", "Repository.cs"))
            .Project.Solution;

        return solution;
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
