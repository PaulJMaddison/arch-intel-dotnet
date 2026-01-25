using ArchIntel.Analysis;
using ArchIntel.IO;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ImpactAnalysisEngineTests
{
    [Fact]
    public async Task AnalyzeAsync_FindsSymbolAndReferences()
    {
        var (solution, engine) = CreateSolution();

        var result = await engine.AnalyzeAsync(solution, "test-version", "Alpha.Widget", CancellationToken.None);

        Assert.True(result.Found);
        Assert.NotNull(result.DefinitionLocation);
        Assert.Contains("Beta", result.ImpactedProjects);
        Assert.Contains(NormalizePath("/repo/src/Beta/UseWidget.cs"), result.ImpactedFiles.Select(NormalizePath));
        Assert.Equal(1, result.TotalReferences);
        Assert.Empty(result.Suggestions);
    }

    [Fact]
    public async Task AnalyzeAsync_ReturnsSuggestionsWhenSymbolMissing()
    {
        var (solution, engine) = CreateSolution();

        var result = await engine.AnalyzeAsync(solution, "test-version", "Alpha.Widgit", CancellationToken.None);

        Assert.False(result.Found);
        Assert.Contains("Alpha.Widget", result.Suggestions);
        Assert.Empty(result.ImpactedProjects);
        Assert.Equal(0, result.TotalReferences);
    }

    [Fact]
    public async Task AnalyzeAsync_TracksCrossProjectReferences()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var references = CreateReferences();

        var projectA = ProjectId.CreateNewId();
        var projectB = ProjectId.CreateNewId();
        var projectC = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(projectA, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp, filePath: "/repo/src/Alpha/Alpha.csproj", metadataReferences: references));
        solution = solution.AddProject(ProjectInfo.Create(projectB, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp, filePath: "/repo/src/Beta/Beta.csproj", metadataReferences: references));
        solution = solution.AddProject(ProjectInfo.Create(projectC, VersionStamp.Create(), "Gamma", "Gamma", LanguageNames.CSharp, filePath: "/repo/src/Gamma/Gamma.csproj", metadataReferences: references));

        solution = solution.AddProjectReference(projectB, new ProjectReference(projectA));
        solution = solution.AddProjectReference(projectC, new ProjectReference(projectA));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectA),
            "Service.cs",
            "namespace Alpha; public class Service { }",
            filePath: "/repo/src/Alpha/Service.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectB),
            "UseService.cs",
            "using Alpha; namespace Beta; public class UseService { public Service Field = new(); }",
            filePath: "/repo/src/Beta/UseService.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectC),
            "OtherUseService.cs",
            "using Alpha; namespace Gamma; public class OtherUseService { public Service Field = new(); }",
            filePath: "/repo/src/Gamma/OtherUseService.cs");

        var engine = CreateEngine();

        var result = await engine.AnalyzeAsync(solution, "test-version", "Alpha.Service", CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(2, result.TotalReferences);
        Assert.Equal(new[] { "Beta", "Gamma" }, result.ImpactedProjects);
        Assert.Equal(
            new[] { "/repo/src/Beta/UseService.cs", "/repo/src/Gamma/OtherUseService.cs" }.Select(NormalizePath),
            result.ImpactedFiles.Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
    }

    private static (Solution Solution, ImpactAnalysisEngine Engine) CreateSolution()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var references = CreateReferences();

        var projectA = ProjectId.CreateNewId();
        var projectB = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(projectA, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp, filePath: "/repo/src/Alpha/Alpha.csproj", metadataReferences: references));
        solution = solution.AddProject(ProjectInfo.Create(projectB, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp, filePath: "/repo/src/Beta/Beta.csproj", metadataReferences: references));

        solution = solution.AddProjectReference(projectB, new ProjectReference(projectA));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectA),
            "Widget.cs",
            "namespace Alpha; public class Widget { }",
            filePath: "/repo/src/Alpha/Widget.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectB),
            "UseWidget.cs",
            "using Alpha; namespace Beta; public class UseWidget { public Widget Field = new(); }",
            filePath: "/repo/src/Beta/UseWidget.cs");

        return (solution, CreateEngine());
    }

    private static ImpactAnalysisEngine CreateEngine()
    {
        var fileSystem = new PhysicalFileSystem();
        var hashService = new DocumentHashService(fileSystem);
        var cache = new DocumentCache(new InMemoryCacheStore());
        return new ImpactAnalysisEngine(new DocumentFilter(), hashService, cache, maxDegreeOfParallelism: 2);
    }

    private static MetadataReference[] CreateReferences()
    {
        return [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
