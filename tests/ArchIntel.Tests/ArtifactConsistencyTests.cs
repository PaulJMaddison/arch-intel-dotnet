using System.Text.RegularExpressions;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ArtifactConsistencyTests
{
    [Fact]
    public async Task CanonicalProjectIds_AreConsistentAcrossProjectsGraphAndSymbols()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var appId = ProjectId.CreateNewId();
        var libId = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(appId, VersionStamp.Create(), "App", "App", LanguageNames.CSharp, filePath: "/repo/src/App/App.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(libId, VersionStamp.Create(), "Lib", "Lib", LanguageNames.CSharp, filePath: "/repo/src/Lib/Lib.csproj"));
        solution = solution.AddProjectReference(appId, new ProjectReference(libId));
        solution = solution.AddDocument(DocumentId.CreateNewId(appId), "Program.cs", "namespace App; public class Program { }", filePath: "/repo/src/App/Program.cs");

        var context = CreateContext(solution);
        var projects = ProjectsReport.Create(context);
        var graph = ProjectGraphReport.Create(context);

        var index = new SymbolIndex(new DocumentFilter(), new DocumentHashService(new PhysicalFileSystem()), new DocumentCache(new InMemoryCacheStore()), 1);
        var symbols = await index.BuildAsync(solution, "v", CancellationToken.None, "/repo");

        var projectIds = projects.Projects.Select(p => p.ProjectId).ToHashSet(StringComparer.Ordinal);
        var guidRegex = new Regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.Compiled);

        Assert.All(symbols.Symbols, symbol => Assert.Contains(symbol.ProjectId, projectIds));
        Assert.All(symbols.Namespaces, projectNamespace =>
        {
            Assert.DoesNotMatch(guidRegex, projectNamespace.ProjectId);
            Assert.Contains(projectNamespace.ProjectId, projectIds);
        });
        Assert.All(graph.Nodes, node => Assert.Contains(node.Id, projectIds));
    }

    [Fact]
    public void ProjectFacts_DetectsTestProjectsAndNonTestProjects()
    {
        using var temp = new TemporaryDirectory();
        var repo = temp.Path;
        Directory.CreateDirectory(Path.Combine(repo, "src", "App"));
        Directory.CreateDirectory(Path.Combine(repo, "tests", "App.Tests"));
        var appProj = Path.Combine(repo, "src", "App", "App.csproj");
        var testProj = Path.Combine(repo, "tests", "App.Tests", "App.Tests.csproj");

        File.WriteAllText(appProj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(testProj, "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /></ItemGroup></Project>");

        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var appId = ProjectId.CreateNewId();
        var testsId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(appId, VersionStamp.Create(), "App", "App", LanguageNames.CSharp, filePath: appProj));
        solution = solution.AddProject(ProjectInfo.Create(testsId, VersionStamp.Create(), "App.Tests", "App.Tests", LanguageNames.CSharp, filePath: testProj));

        var appFacts = ProjectFacts.Get(solution.GetProject(appId)!, repo, new AnalysisConfig());
        var testFacts = ProjectFacts.Get(solution.GetProject(testsId)!, repo, new AnalysisConfig());

        Assert.False(appFacts.IsTestProject);
        Assert.Equal(TestDetectionReason.DefaultFalse, appFacts.TestDetectionReason);
        Assert.True(testFacts.IsTestProject);
        Assert.Equal(TestDetectionReason.MicrosoftNetTestSdk, testFacts.TestDetectionReason);
    }

    [Fact]
    public void ProjectFacts_DoesNotTreatBunitOrPlaywrightAloneAsTestProjects()
    {
        using var temp = new TemporaryDirectory();
        var repo = temp.Path;
        var bunitProj = CreateProject(repo, "src/Ui/Ui.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"bunit\" Version=\"1.0.0\" /></ItemGroup></Project>");
        var playwrightProj = CreateProject(repo, "src/Automation/Automation.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.Playwright\" Version=\"1.0.0\" /></ItemGroup></Project>");
        var namedTestProj = CreateProject(repo, "tests/Ui.UITests/Ui.UITests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"bunit\" Version=\"1.0.0\" /></ItemGroup></Project>");

        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var bunitId = ProjectId.CreateNewId();
        var playwrightId = ProjectId.CreateNewId();
        var namedTestId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(bunitId, VersionStamp.Create(), "Ui", "Ui", LanguageNames.CSharp, filePath: bunitProj));
        solution = solution.AddProject(ProjectInfo.Create(playwrightId, VersionStamp.Create(), "Automation", "Automation", LanguageNames.CSharp, filePath: playwrightProj));
        solution = solution.AddProject(ProjectInfo.Create(namedTestId, VersionStamp.Create(), "Ui.UITests", "Ui.UITests", LanguageNames.CSharp, filePath: namedTestProj));

        var bunitFacts = ProjectFacts.Get(solution.GetProject(bunitId)!, repo, new AnalysisConfig());
        var playwrightFacts = ProjectFacts.Get(solution.GetProject(playwrightId)!, repo, new AnalysisConfig());
        var namedTestFacts = ProjectFacts.Get(solution.GetProject(namedTestId)!, repo, new AnalysisConfig());

        Assert.False(bunitFacts.IsTestProject);
        Assert.Equal(TestDetectionReason.DefaultFalse, bunitFacts.TestDetectionReason);
        Assert.False(playwrightFacts.IsTestProject);
        Assert.Equal(TestDetectionReason.DefaultFalse, playwrightFacts.TestDetectionReason);
        Assert.True(namedTestFacts.IsTestProject);
        Assert.Equal(TestDetectionReason.NamePattern, namedTestFacts.TestDetectionReason);
    }

    private static string CreateProject(string repoRoot, string relativePath, string content)
    {
        var path = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static AnalysisContext CreateContext(Solution solution)
    {
        return new AnalysisContext("/repo/app.sln", "/repo", solution, new AnalysisConfig(), NullLogger.Instance, solution.Projects.Count(), 0);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "archintel-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
