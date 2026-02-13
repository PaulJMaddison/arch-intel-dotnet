using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class InsightsReportTests
{
    [Fact]
    public void InsightsReport_ComputesFanInAndFanOutDeterministically()
    {
        var solution = CreateGraphSolution();
        var context = CreateContext(solution, "/repo");
        var symbolData = new SymbolIndexData(Array.Empty<SymbolIndexEntry>(), Array.Empty<ProjectNamespaceStats>());

        var data = InsightsReport.Create(context, symbolData);

        Assert.Equal(new[] { "Core", "Api", "Data", "App", "Tests" }, data.TopFanInProjects.Select(item => item.ProjectName).ToArray());
        Assert.Equal(new[] { "Api", "App", "Tests", "Data", "Core" }, data.TopFanOutProjects.Select(item => item.ProjectName).ToArray());

        var coreTop = data.CoreProjects[0];
        Assert.Equal("Core", coreTop.ProjectName);
        Assert.Equal(3, coreTop.FanIn);
        Assert.Equal(0, coreTop.FanOut);

        var risky = data.RiskyProjects.Select(item => item.ProjectName).ToArray();
        Assert.Equal(new[] { "Api", "App", "Tests" }, risky);

        var cycle = Assert.Single(data.CycleSeverity);
        Assert.Equal(3, cycle.Length);
        Assert.True(cycle.ContainsTestProject);
        Assert.Equal(7, cycle.TotalFanOut);
        Assert.Equal(370, cycle.SeverityScore);
    }

    [Fact]
    public async Task InsightsReport_WriteAsync_ProducesDeterministicSnapshot()
    {
        using var temp = new TemporaryDirectory();
        var outputDir = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(outputDir);
        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);

        var appPath = CreateProjectFile(repoRoot, "src/App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
                <PackageReference Include="Serilog" Version="3.0.0-beta1" />
              </ItemGroup>
            </Project>
            """);
        var corePath = CreateProjectFile(repoRoot, "src/Core/Core.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="12.0.3" />
              </ItemGroup>
            </Project>
            """);
        var testsPath = CreateProjectFile(repoRoot, "tests/App.Tests/App.Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.8.1" />
              </ItemGroup>
            </Project>
            """);

        var solution = CreateSnapshotSolution(appPath, corePath, testsPath);
        var context = CreateContext(solution, repoRoot, outputDir);

        await InsightsReport.WriteAsync(context, new ArchIntel.IO.PhysicalFileSystem(), outputDir, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "insights.json"));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("insights", root.GetProperty("Kind").GetString());

        var topFanOut = root.GetProperty("TopFanOutProjects").EnumerateArray().Select(item => item.GetProperty("ProjectName").GetString()).ToArray();
        Assert.Equal(new[] { "App", "App.Tests", "Core" }, topFanOut);

        var packageHotspots = root.GetProperty("PackageDriftHotspots").EnumerateArray().ToArray();
        Assert.Equal(2, packageHotspots.Length);
        Assert.Equal("Newtonsoft.Json", packageHotspots[0].GetProperty("PackageId").GetString());
        Assert.Equal(2, packageHotspots[0].GetProperty("DistinctMajorCount").GetInt32());
        Assert.Equal("Serilog", packageHotspots[1].GetProperty("PackageId").GetString());
        Assert.True(packageHotspots[1].GetProperty("HasPrerelease").GetBoolean());

        var rules = root.GetProperty("DeterministicRules").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(InsightsReport.DeterministicRules.Count, rules.Length);
        Assert.Equal(InsightsReport.DeterministicRules[0], rules[0]);
    }

    [Fact]
    public void ScanReceipt_IncludesInsightsDeterministicRules()
    {
        var solution = CreateGraphSolution();
        var context = CreateContext(solution, "/repo");

        var data = ScanReceiptReport.Create(context);

        Assert.NotEmpty(data.DeterministicRules);
        Assert.Contains(data.DeterministicRules, rule => rule.Contains("cycle_severity_score", StringComparison.Ordinal));
        Assert.Equal(data.DeterministicRules.OrderBy(rule => rule, StringComparer.Ordinal), data.DeterministicRules);
        Assert.All(data.DeterministicRules, rule =>
        {
            Assert.DoesNotContain('\r', rule);
            Assert.DoesNotContain('\n', rule);
        });
    }

    [Fact]
    public void DeterministicRuleFormatter_SanitizesAndSortsRules()
    {
        var rules = new[]
        {
            "zeta rule\r\nwith newline",
            " alpha\t\t rule ",
            "beta\nrule"
        };

        var formatted = DeterministicRuleFormatter.SanitizeAndSort(rules);

        Assert.Equal(new[] { "alpha rule", "beta rule", "zeta rule with newline" }, formatted);
        Assert.All(formatted, rule => Assert.False(string.IsNullOrWhiteSpace(rule)));
    }

    private static Solution CreateGraphSolution()
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();

        var app = ProjectId.CreateNewId();
        var api = ProjectId.CreateNewId();
        var core = ProjectId.CreateNewId();
        var data = ProjectId.CreateNewId();
        var tests = ProjectId.CreateNewId();

        var projects = new[]
        {
            ProjectInfo.Create(app, VersionStamp.Create(), "App", "App", LanguageNames.CSharp, filePath: "/repo/src/App/App.csproj", projectReferences: [new ProjectReference(api), new ProjectReference(core)]),
            ProjectInfo.Create(api, VersionStamp.Create(), "Api", "Api", LanguageNames.CSharp, filePath: "/repo/src/Api/Api.csproj", projectReferences: [new ProjectReference(core), new ProjectReference(data), new ProjectReference(tests)]),
            ProjectInfo.Create(core, VersionStamp.Create(), "Core", "Core", LanguageNames.CSharp, filePath: "/repo/src/Core/Core.csproj"),
            ProjectInfo.Create(data, VersionStamp.Create(), "Data", "Data", LanguageNames.CSharp, filePath: "/repo/src/Data/Data.csproj", projectReferences: [new ProjectReference(core)]),
            ProjectInfo.Create(tests, VersionStamp.Create(), "Tests", "Tests", LanguageNames.CSharp, filePath: "/repo/tests/Tests/Tests.csproj", projectReferences: [new ProjectReference(app), new ProjectReference(api)])
        };

        return workspace.AddSolution(SolutionInfo.Create(solutionId, VersionStamp.Create(), projects: projects));
    }

    private static Solution CreateSnapshotSolution(string appPath, string corePath, string testsPath)
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();

        var app = ProjectId.CreateNewId();
        var core = ProjectId.CreateNewId();
        var tests = ProjectId.CreateNewId();

        var projects = new[]
        {
            ProjectInfo.Create(app, VersionStamp.Create(), "App", "App", LanguageNames.CSharp, filePath: appPath, projectReferences: [new ProjectReference(core)]),
            ProjectInfo.Create(core, VersionStamp.Create(), "Core", "Core", LanguageNames.CSharp, filePath: corePath),
            ProjectInfo.Create(tests, VersionStamp.Create(), "App.Tests", "App.Tests", LanguageNames.CSharp, filePath: testsPath, projectReferences: [new ProjectReference(app)])
        };

        return workspace.AddSolution(SolutionInfo.Create(solutionId, VersionStamp.Create(), projects: projects));
    }

    private static string CreateProjectFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static AnalysisContext CreateContext(Solution solution, string repoRoot, string? outputDir = null)
    {
        var output = outputDir ?? Path.Combine(repoRoot, ".archintel");
        var config = new AnalysisConfig
        {
            OutputDir = output,
            CacheDir = Path.Combine(output, "cache"),
            MaxDegreeOfParallelism = 1
        };

        return new AnalysisContext(
            Path.Combine(repoRoot, "arch.sln"),
            repoRoot,
            solution,
            config,
            NullLogger.Instance,
            solution.Projects.Count(),
            0);
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
