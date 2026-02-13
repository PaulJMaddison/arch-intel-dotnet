using System.Text.Json;
using System.Text.RegularExpressions;
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
    public async Task ScanReport_NamespacesJoinProjects_AndTotalsReconcile()
    {
        using var temp = new TemporaryDirectory();
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Test", "Test", LanguageNames.CSharp, filePath: "/repo/src/Test/Test.csproj"))
            .AddDocument(DocumentId.CreateNewId(projectId), "Test.cs", "namespace Test; public class C { public void M(){} }", filePath: "/repo/src/Test/Test.cs");
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
            loadDiagnostics: new[] { new LoadDiagnostic("Warning", "NU1605 Detected package downgrade.", false) });

        var writer = new ReportWriter();
        _ = await writer.WriteAsync(context, "scan", null, ReportFormat.Json, CancellationToken.None);

        using var projectsDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "projects.json")));
        var projectIds = projectsDocument.RootElement.GetProperty("Projects")
            .EnumerateArray()
            .Select(project => project.GetProperty("ProjectId").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        using var namespacesDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "namespaces.json")));
        var guidRegex = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);

        var declaredTotal = 0;
        var reachableTotal = 0;
        var methodsTotal = 0;
        var internalTotal = 0;

        foreach (var project in namespacesDocument.RootElement.EnumerateArray())
        {
            var namespaceProjectId = project.GetProperty("ProjectId").GetString();
            Assert.NotNull(namespaceProjectId);
            Assert.DoesNotMatch(guidRegex, namespaceProjectId!);
            Assert.Contains(namespaceProjectId!, projectIds);

            Assert.True(project.TryGetProperty("ProjectPath", out _));
            Assert.True(project.TryGetProperty("RoslynProjectId", out _));

            var namespaces = project.GetProperty("Namespaces").EnumerateArray().ToArray();
            Assert.Equal(namespaces.OrderBy(ns => ns.GetProperty("Name").GetString(), StringComparer.Ordinal).Select(ns => ns.GetProperty("Name").GetString()),
                namespaces.Select(ns => ns.GetProperty("Name").GetString()));

            foreach (var ns in namespaces)
            {
                declaredTotal += ns.GetProperty("DeclaredPublicMethodCount").GetInt32();
                reachableTotal += ns.GetProperty("PubliclyReachableMethodCount").GetInt32();
                methodsTotal += ns.GetProperty("TotalMethodCount").GetInt32();
                internalTotal += ns.GetProperty("InternalMethodCount").GetInt32();

                var topTypes = ns.GetProperty("TopTypes").EnumerateArray().ToArray();
                var ordered = topTypes
                    .OrderByDescending(type => type.GetProperty("DeclaredPublicMethodCount").GetInt32())
                    .ThenBy(type => type.GetProperty("Name").GetString(), StringComparer.Ordinal)
                    .Select(type => type.GetProperty("Name").GetString())
                    .ToArray();
                Assert.Equal(ordered, topTypes.Select(type => type.GetProperty("Name").GetString()).ToArray());
            }
        }

        using var summaryDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "scan_summary.json")));
        var methodCounts = summaryDocument.RootElement.GetProperty("MethodCounts");
        Assert.Equal(declaredTotal, methodCounts.GetProperty("DeclaredPublicMethodsTotal").GetInt32());
        Assert.Equal(reachableTotal, methodCounts.GetProperty("PubliclyReachableMethodsTotal").GetInt32());
        Assert.Equal(methodsTotal, methodCounts.GetProperty("TotalMethodsTotal").GetInt32());
        Assert.Equal(internalTotal, methodCounts.GetProperty("InternalMethodsTotal").GetInt32());

        using var symbolsDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputDir, "symbols.json")));
        foreach (var symbol in symbolsDocument.RootElement.EnumerateArray())
        {
            Assert.True(symbol.TryGetProperty("ProjectId", out var symbolProjectId));
            Assert.Contains(symbolProjectId.GetString()!, projectIds);
        }
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
