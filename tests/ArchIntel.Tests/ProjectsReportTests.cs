using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ProjectsReportTests
{
    [Fact]
    public void ProjectsReport_EmitsProjectReferences()
    {
        using var temp = new TemporaryDirectory();
        var solution = CreateChainSolution();
        var context = CreateContext(solution, temp.Path);

        var data = ProjectsReport.Create(context);

        Assert.Equal(3, data.Graph.TotalProjects);
        Assert.Equal(2, data.Graph.TotalEdges);
        Assert.False(data.Graph.CyclesDetected);

        var projectsByName = data.Projects.ToDictionary(project => project.ProjectName, StringComparer.Ordinal);

        Assert.Collection(
            projectsByName["Alpha"].ProjectReferences,
            reference => Assert.Equal("Beta", reference.ProjectName));
        Assert.Collection(
            projectsByName["Beta"].ProjectReferences,
            reference => Assert.Equal("Gamma", reference.ProjectName));
        Assert.Empty(projectsByName["Gamma"].ProjectReferences);
    }

    [Fact]
    public async Task ProjectsReport_WritesProjectsJsonWithCycles()
    {
        using var temp = new TemporaryDirectory();
        var outputDir = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(outputDir);
        var solution = CreateCycleSolution();
        var context = CreateContext(solution, outputDir);

        await ProjectsReport.WriteAsync(context, new PhysicalFileSystem(), outputDir, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "projects.json"));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var graph = root.GetProperty("Graph");
        Assert.True(graph.GetProperty("CyclesDetected").GetBoolean());
        Assert.Equal(2, graph.GetProperty("TotalProjects").GetInt32());
    }

    private static Solution CreateChainSolution()
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();

        var alphaId = ProjectId.CreateNewId();
        var betaId = ProjectId.CreateNewId();
        var gammaId = ProjectId.CreateNewId();

        var alpha = ProjectInfo.Create(
            alphaId,
            VersionStamp.Create(),
            "Alpha",
            "Alpha",
            LanguageNames.CSharp,
            filePath: "/repo/src/Alpha/Alpha.csproj",
            projectReferences: [new ProjectReference(betaId)]);
        var beta = ProjectInfo.Create(
            betaId,
            VersionStamp.Create(),
            "Beta",
            "Beta",
            LanguageNames.CSharp,
            filePath: "/repo/src/Beta/Beta.csproj",
            projectReferences: [new ProjectReference(gammaId)]);
        var gamma = ProjectInfo.Create(
            gammaId,
            VersionStamp.Create(),
            "Gamma",
            "Gamma",
            LanguageNames.CSharp,
            filePath: "/repo/src/Gamma/Gamma.csproj");

        return workspace.AddSolution(
            SolutionInfo.Create(solutionId, VersionStamp.Create(), projects: [alpha, beta, gamma]));
    }

    private static Solution CreateCycleSolution()
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();

        var alphaId = ProjectId.CreateNewId();
        var betaId = ProjectId.CreateNewId();

        var alpha = ProjectInfo.Create(
            alphaId,
            VersionStamp.Create(),
            "Alpha",
            "Alpha",
            LanguageNames.CSharp,
            filePath: "/repo/src/Alpha/Alpha.csproj",
            projectReferences: [new ProjectReference(betaId)]);
        var beta = ProjectInfo.Create(
            betaId,
            VersionStamp.Create(),
            "Beta",
            "Beta",
            LanguageNames.CSharp,
            filePath: "/repo/src/Beta/Beta.csproj",
            projectReferences: [new ProjectReference(alphaId)]);

        return workspace.AddSolution(
            SolutionInfo.Create(solutionId, VersionStamp.Create(), projects: [alpha, beta]));
    }

    private static AnalysisContext CreateContext(Solution solution, string outputDir)
    {
        var config = new AnalysisConfig
        {
            OutputDir = outputDir,
            CacheDir = Path.Combine(outputDir, "cache"),
            MaxDegreeOfParallelism = 1
        };

        return new AnalysisContext(
            "/repo/arch.sln",
            "/repo",
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
