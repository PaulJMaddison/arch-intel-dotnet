using ArchIntel.Analysis;
using ArchIntel.IO;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class SymbolIndexTests
{
    [Fact]
    public async Task BuildAsync_IndexesSymbolsAcrossProjects()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectA = ProjectId.CreateNewId();
        var projectB = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(projectA, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp, filePath: "/repo/src/Alpha/Alpha.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(projectB, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp, filePath: "/repo/src/Beta/Beta.csproj"));

        solution = solution.AddDocument(
                DocumentId.CreateNewId(projectA),
                "Widget.cs",
                @"namespace Alpha; public class Widget { public void Ping() { } internal void Hidden() { } }",
                filePath: "/repo/src/Alpha/Widget.cs")
            .Project.Solution;

        solution = solution.AddDocument(
                DocumentId.CreateNewId(projectB),
                "Gadget.cs",
                @"namespace Beta { public class Gadget { public int Calc() => 1; } }",
                filePath: "/repo/src/Beta/Gadget.cs")
            .Project.Solution;

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        Assert.Contains(data.Symbols, symbol => symbol.ProjectName == "Alpha" && symbol.Name == "Widget" && symbol.Kind == "NamedType");
        Assert.Contains(data.Symbols, symbol => symbol.ProjectName == "Alpha" && symbol.Name == "Ping" && symbol.Kind == "PublicMethod");
        Assert.DoesNotContain(data.Symbols, symbol => symbol.ProjectName == "Alpha" && symbol.Name == "Hidden");
        Assert.Contains(data.Symbols, symbol => symbol.ProjectName == "Beta" && symbol.Name == "Gadget" && symbol.Kind == "NamedType");

        var alphaNamespaces = data.Namespaces.Single(stats => stats.ProjectName == "Alpha").Namespaces;
        var alphaNamespace = alphaNamespaces.Single(stat => stat.Name == "Alpha");
        Assert.Equal(1, alphaNamespace.NamedTypeCount);
        Assert.Equal(1, alphaNamespace.PublicMethodCount);
    }

    [Fact]
    public async Task BuildAsync_KeepsCrossProjectSymbolsDistinct()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectA = ProjectId.CreateNewId();
        var projectB = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(projectA, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp, filePath: "/repo/src/Alpha/Alpha.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(projectB, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp, filePath: "/repo/src/Beta/Beta.csproj"));

        solution = solution.AddDocument(
                DocumentId.CreateNewId(projectA),
                "Shared.cs",
                @"namespace Shared; public class SharedType { }",
                filePath: "/repo/src/Alpha/Shared.cs")
            .Project.Solution;

        solution = solution.AddDocument(
                DocumentId.CreateNewId(projectB),
                "Shared.cs",
                @"namespace Shared; public class SharedType { }",
                filePath: "/repo/src/Beta/Shared.cs")
            .Project.Solution;

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        var sharedSymbols = data.Symbols.Where(symbol => symbol.Name == "SharedType").ToArray();
        Assert.Equal(2, sharedSymbols.Length);
        Assert.Equal(2, sharedSymbols.Select(symbol => symbol.ProjectName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, sharedSymbols.Select(symbol => symbol.SymbolId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task BuildAsync_DeduplicatesPartialDeclarations()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectA = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(projectA, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp, filePath: "/repo/src/Alpha/Alpha.csproj"));

        solution = solution.AddDocument(
                DocumentId.CreateNewId(projectA),
                "Part1.cs",
                @"namespace Dup; public partial class DupType { }",
                filePath: "/repo/src/Alpha/Part1.cs")
            .Project.Solution;

        solution = solution.AddDocument(
                DocumentId.CreateNewId(projectA),
                "Part2.cs",
                @"namespace Dup; public partial class DupType { public void Run() { } }",
                filePath: "/repo/src/Alpha/Part2.cs")
            .Project.Solution;

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        var namedTypes = data.Symbols.Where(symbol => symbol.Kind == "NamedType" && symbol.Name == "DupType").ToArray();
        Assert.Single(namedTypes);
        Assert.Contains(data.Symbols, symbol => symbol.Kind == "PublicMethod" && symbol.Name == "Run");
    }

    private static SymbolIndex CreateIndex()
    {
        var fileSystem = new PhysicalFileSystem();
        var hashService = new DocumentHashService(fileSystem);
        var cache = new DocumentCache(new InMemoryCacheStore());
        return new SymbolIndex(new DocumentFilter(), hashService, cache, maxDegreeOfParallelism: 2);
    }
}
