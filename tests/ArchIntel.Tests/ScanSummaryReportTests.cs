using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ScanSummaryReportTests
{
    [Fact]
    public async Task ScanSummaryReport_WritesExpectedSchema()
    {
        using var temp = new TemporaryDirectory();
        var outputDir = Path.Combine(temp.Path, "output");
        var cacheDir = Path.Combine(temp.Path, "cache");
        Directory.CreateDirectory(outputDir);
        var solution = CreateSolutionWithDocument();
        var config = new AnalysisConfig
        {
            OutputDir = outputDir,
            CacheDir = cacheDir,
            MaxDegreeOfParallelism = 1
        };

        var context = new AnalysisContext(
            "/repo/arch.sln",
            "/repo",
            solution,
            config,
            NullLogger.Instance,
            solution.Projects.Count(),
            0,
            loadDiagnostics: new[]
            {
                new LoadDiagnostic("Warning", "NU1605 Detected package downgrade.", false)
            });

        await ScanSummaryReport.WriteAsync(context, new PhysicalFileSystem(), outputDir, CancellationToken.None);

        var summaryPath = Path.Combine(outputDir, "scan_summary.json");
        var json = await File.ReadAllTextAsync(summaryPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("Counts", out var counts));
        Assert.Equal(solution.Projects.Count(), counts.GetProperty("ProjectCount").GetInt32());
        Assert.Equal(0, counts.GetProperty("FailedProjectCount").GetInt32());
        Assert.Equal(1, counts.GetProperty("AnalyzedDocuments").GetInt32());

        Assert.True(root.TryGetProperty("MethodCounts", out var methodCounts));
        Assert.Equal(0, methodCounts.GetProperty("DeclaredPublicMethodsTotal").GetInt32());
        Assert.Equal(0, methodCounts.GetProperty("PubliclyReachableMethodsTotal").GetInt32());
        Assert.Equal(0, methodCounts.GetProperty("TotalMethodsTotal").GetInt32());
        Assert.Equal(0, methodCounts.GetProperty("InternalMethodsTotal").GetInt32());

        Assert.True(root.TryGetProperty("LoadDiagnostics", out var loadDiagnostics));
        Assert.Equal(JsonValueKind.Array, loadDiagnostics.ValueKind);
        Assert.Equal(1, loadDiagnostics.GetArrayLength());
        Assert.Equal("Warning", loadDiagnostics[0].GetProperty("Kind").GetString());
    }

    [Fact]
    public void ScanReceiptMarkdown_IncludesPublicSurfaceSections()
    {
        var data = new ScanReceiptReportData(
            "scan",
            "/repo/arch.sln",
            "v1",
            "v1",
            null,
            "arch scan --solution /repo/arch.sln",
            "/repo/out",
            "/repo/cache",
            1,
            false,
            null,
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { new ScanReceiptProject("p1", "r1", "App", "src/App/App.csproj") },
            InsightsReport.DeterministicRules);

        var symbolData = new SymbolIndexData(
            Array.Empty<SymbolIndexEntry>(),
            new[]
            {
                new ProjectNamespaceStats(
                    "App",
                    "p1",
                    "r1",
                    "src/App/App.csproj",
                    new[]
                    {
                        new NamespaceStat(
                            "App.Api",
                            1,
                            1,
                            2,
                            2,
                            3,
                            1,
                            new[] { new TopTypeStat("Controller", "public", 2, 2, 3) })
                    })
            });

        var markdown = ScanReceiptReport.BuildMarkdown(data, symbolData);

        Assert.Contains("## Top namespaces by public surface", markdown, StringComparison.Ordinal);
        Assert.Contains("## Top types per namespace", markdown, StringComparison.Ordinal);
        Assert.Contains("Controller [public]", markdown, StringComparison.Ordinal);
    }

    private static Solution CreateSolutionWithDocument()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "App",
            "App",
            LanguageNames.CSharp,
            filePath: "/repo/src/App/App.csproj"));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Program.cs",
            SourceText.From("namespace App; public class Program { }") ,
            filePath: "/repo/src/App/Program.cs");

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
