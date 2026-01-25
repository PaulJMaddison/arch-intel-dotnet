using ArchIntel.Analysis;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ProjectGraphBuilderTests
{
    [Fact]
    public void Build_CreatesNodesAndEdgesFromProjectReferences()
    {
        var solution = CreateSolution();

        var graph = ProjectGraphBuilder.Build(solution, "/repo");

        Assert.Equal(2, graph.Nodes.Count);
        Assert.Single(graph.Edges);

        var nodesByName = graph.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        Assert.Contains(graph.Edges, edge => edge.FromId == nodesByName["App"].Id && edge.ToId == nodesByName["Lib"].Id);
        Assert.Equal(
            graph.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal),
            graph.Nodes.Select(node => node.Id));
    }

    [Fact]
    public void Build_DetectsCycles()
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();

        var projectA = ProjectId.CreateNewId();
        var projectB = ProjectId.CreateNewId();

        var projectInfoA = ProjectInfo.Create(
            projectA,
            VersionStamp.Create(),
            "Alpha",
            "Alpha",
            LanguageNames.CSharp,
            filePath: "/repo/src/Alpha/Alpha.csproj",
            projectReferences: [new ProjectReference(projectB)]);
        var projectInfoB = ProjectInfo.Create(
            projectB,
            VersionStamp.Create(),
            "Beta",
            "Beta",
            LanguageNames.CSharp,
            filePath: "/repo/src/Beta/Beta.csproj",
            projectReferences: [new ProjectReference(projectA)]);

        var solution = workspace.AddSolution(
            SolutionInfo.Create(solutionId, VersionStamp.Create(), projects: [projectInfoA, projectInfoB]));

        var graph = ProjectGraphBuilder.Build(solution, "/repo");

        Assert.Single(graph.Cycles);

        var nodesByName = graph.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        var expectedCycle = new[] { nodesByName["Alpha"].Id, nodesByName["Beta"].Id }
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedCycle, graph.Cycles[0]);
    }

    [Fact]
    public void Build_ClassifiesLayers()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "MyApp.Api", "MyApp.Api", LanguageNames.CSharp, filePath: "/repo/src/MyApp.Api/MyApp.Api.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "MyApp.Application", "MyApp.Application", LanguageNames.CSharp, filePath: "/repo/src/MyApp.Application/MyApp.Application.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "MyApp.Domain", "MyApp.Domain", LanguageNames.CSharp, filePath: "/repo/src/MyApp.Domain/MyApp.Domain.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "MyApp.Infrastructure", "MyApp.Infrastructure", LanguageNames.CSharp, filePath: "/repo/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "MyApp.Tests", "MyApp.Tests", LanguageNames.CSharp, filePath: "/repo/tests/MyApp.Tests/MyApp.Tests.csproj"));

        var graph = ProjectGraphBuilder.Build(solution, "/repo");

        var nodesByName = graph.Nodes.ToDictionary(node => node.Name, StringComparer.Ordinal);
        Assert.Equal("Presentation", nodesByName["MyApp.Api"].Layer);
        Assert.Equal("Application", nodesByName["MyApp.Application"].Layer);
        Assert.Equal("Domain", nodesByName["MyApp.Domain"].Layer);
        Assert.Equal("Infrastructure", nodesByName["MyApp.Infrastructure"].Layer);
        Assert.Equal("Tests", nodesByName["MyApp.Tests"].Layer);
    }

    private static Solution CreateSolution()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var appId = ProjectId.CreateNewId();
        var libId = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(appId, VersionStamp.Create(), "App", "App", LanguageNames.CSharp, filePath: "/repo/src/App/App.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(libId, VersionStamp.Create(), "Lib", "Lib", LanguageNames.CSharp, filePath: "/repo/src/Lib/Lib.csproj"));

        solution = solution.AddProjectReference(appId, new ProjectReference(libId));

        return solution;
    }
}
