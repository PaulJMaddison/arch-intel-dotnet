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
            filePath: "/repo/src/Alpha/Widget.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectB),
            "Gadget.cs",
            @"namespace Beta { public class Gadget { public int Calc() => 1; } }",
            filePath: "/repo/src/Beta/Gadget.cs");

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        Assert.Contains(data.Symbols, symbol => symbol.ProjectName == "Alpha" && symbol.Name == "Widget" && symbol.Kind == "NamedType");
        Assert.Contains(data.Symbols, symbol => symbol.ProjectName == "Alpha" && symbol.Name == "Ping" && symbol.Kind == "PublicMethod");
        Assert.DoesNotContain(data.Symbols, symbol => symbol.ProjectName == "Alpha" && symbol.Name == "Hidden");
        Assert.Contains(data.Symbols, symbol => symbol.ProjectName == "Beta" && symbol.Name == "Gadget" && symbol.Kind == "NamedType");

        var alphaNamespaces = data.Namespaces.Single(stats => stats.ProjectName == "Alpha").Namespaces;
        var alphaNamespace = alphaNamespaces.Single(stat => stat.Name == "Alpha");
        Assert.Equal(1, alphaNamespace.PublicTypeCount);
        Assert.Equal(1, alphaNamespace.TotalTypeCount);
        Assert.NotEmpty(alphaNamespace.TopTypes);
        Assert.Equal(1, alphaNamespace.DeclaredPublicMethodCount);
        Assert.Equal(1, alphaNamespace.PubliclyReachableMethodCount);
        Assert.Equal(2, alphaNamespace.TotalMethodCount);
        Assert.Equal(1, alphaNamespace.InternalMethodCount);

        var methodTotals = data.GetMethodCountTotals();
        Assert.Equal(2, methodTotals.DeclaredPublicMethodsTotal);
        Assert.Equal(2, methodTotals.PubliclyReachableMethodsTotal);
        Assert.Equal(3, methodTotals.TotalMethodsTotal);
        Assert.Equal(1, methodTotals.InternalMethodsTotal);
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
            filePath: "/repo/src/Alpha/Shared.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectB),
            "Shared.cs",
            @"namespace Shared; public class SharedType { }",
            filePath: "/repo/src/Beta/Shared.cs");

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
            filePath: "/repo/src/Alpha/Part1.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectA),
            "Part2.cs",
            @"namespace Dup; public partial class DupType { public void Run() { } }",
            filePath: "/repo/src/Alpha/Part2.cs");

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        var namedTypes = data.Symbols.Where(symbol => symbol.Kind == "NamedType" && symbol.Name == "DupType").ToArray();
        Assert.Single(namedTypes);
        Assert.Contains(data.Symbols, symbol => symbol.Kind == "PublicMethod" && symbol.Name == "Run");
    }

    [Fact]
    public async Task BuildAsync_ResolvesNamespacesForNestedFileScopedAndGlobal()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Gamma", "Gamma", LanguageNames.CSharp, filePath: "/repo/src/Gamma/Gamma.csproj"));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Nested.cs",
            @"namespace Outer.Inner { public class NestedType { public void Go() { } } }",
            filePath: "/repo/src/Gamma/Nested.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "FileScoped.cs",
            @"namespace FileScoped; public class FileScopedType { public void Run() { } }",
            filePath: "/repo/src/Gamma/FileScoped.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Global.cs",
            @"public class GlobalType { public void Ping() { } }",
            filePath: "/repo/src/Gamma/Global.cs");

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        Assert.All(data.Symbols, symbol => Assert.NotNull(symbol.Namespace));
        Assert.Contains(data.Symbols, symbol => symbol.Name == "NestedType" && symbol.Namespace == "Outer.Inner");
        Assert.Contains(data.Symbols, symbol => symbol.Name == "Go" && symbol.Namespace == "Outer.Inner");
        Assert.Contains(data.Symbols, symbol => symbol.Name == "FileScopedType" && symbol.Namespace == "FileScoped");
        Assert.Contains(data.Symbols, symbol => symbol.Name == "Run" && symbol.Namespace == "FileScoped");
        Assert.Contains(data.Symbols, symbol => symbol.Name == "GlobalType" && symbol.Namespace == string.Empty);
        Assert.Contains(data.Symbols, symbol => symbol.Name == "Ping" && symbol.Namespace == string.Empty);

        var namespaces = data.Namespaces.Single(stats => stats.ProjectName == "Gamma").Namespaces;
        Assert.Contains(namespaces, stat => stat.Name == "Outer.Inner");
        Assert.Contains(namespaces, stat => stat.Name == "FileScoped");
        Assert.Contains(namespaces, stat => stat.Name == "(global)");
    }

    [Fact]
    public async Task BuildAsync_IndexesPublicMethodsAcrossInterfacesAndRecords()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Delta", "Delta", LanguageNames.CSharp, filePath: "/repo/src/Delta/Delta.csproj"));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Api.cs",
            @"namespace Delta.Api;
              public interface IRunner { void Run(); }
              public class Runner : IRunner
              {
                  public Runner() { }
                  public void Run() { }
                  internal void Hidden() { }
                  private void Secret() { }
                  public int Value { get; set; }
              }
              public record LogRecord
              {
                  public void Write() { }
                  private void Scratch() { }
              }",
            filePath: "/repo/src/Delta/Api.cs");

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        Assert.Contains(data.Symbols, symbol => symbol.Kind == "PublicMethod" && symbol.Name == "Run" && symbol.ContainingType == "IRunner");
        Assert.Contains(data.Symbols, symbol => symbol.Kind == "PublicMethod" && symbol.Name == "Run" && symbol.ContainingType == "Runner");
        Assert.Contains(data.Symbols, symbol => symbol.Kind == "PublicMethod" && symbol.Name == "Write" && symbol.ContainingType == "LogRecord");
        Assert.DoesNotContain(data.Symbols, symbol => symbol.Name == "Hidden");
        Assert.DoesNotContain(data.Symbols, symbol => symbol.Name == "Secret");

        var namespaces = data.Namespaces.Single(stats => stats.ProjectName == "Delta").Namespaces;
        var apiNamespace = namespaces.Single(stat => stat.Name == "Delta.Api");
        Assert.Equal(3, apiNamespace.DeclaredPublicMethodCount);
        Assert.Equal(3, apiNamespace.PubliclyReachableMethodCount);
        Assert.Equal(6, apiNamespace.TotalMethodCount);
        Assert.Equal(3, apiNamespace.InternalMethodCount);
    }



    [Fact]
    public async Task BuildAsync_DistinguishesDeclaredAndPubliclyReachableMethodCounts()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Sigma", "Sigma", LanguageNames.CSharp, filePath: "/repo/src/Sigma/Sigma.csproj"));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Types.cs",
            @"namespace Sigma;
              internal class Foo { public void Bar() { } }
              public class Baz { public void Qux() { } }",
            filePath: "/repo/src/Sigma/Types.cs");

        var index = CreateIndex();
        var data = await index.BuildAsync(solution, "test-version", CancellationToken.None);

        var ns = data.Namespaces.Single(stats => stats.ProjectName == "Sigma").Namespaces.Single(stat => stat.Name == "Sigma");
        Assert.Equal(2, ns.DeclaredPublicMethodCount);
        Assert.Equal(1, ns.PubliclyReachableMethodCount);

        var foo = ns.TopTypes.Single(type => type.Name == "Foo");
        Assert.Equal(1, foo.DeclaredPublicMethodCount);
        Assert.Equal(0, foo.PubliclyReachableMethodCount);

        var baz = ns.TopTypes.Single(type => type.Name == "Baz");
        Assert.Equal(1, baz.DeclaredPublicMethodCount);
        Assert.Equal(1, baz.PubliclyReachableMethodCount);
    }

    [Fact]
    public async Task BuildAsync_EnrichesSymbolMetadataAndRelativePaths_Deterministically()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var projectId = ProjectId.CreateNewId();
        solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Api", "Api", LanguageNames.CSharp, filePath: "/repo/src/Api/Api.csproj"));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Controller.cs",
            @"using System;
              namespace Api.Controllers;
              [ApiController]
              public class BaseController { public virtual void BaseRun() { } }
              public interface IRunner { void Run(); }
              [Authorize]
              public class OrdersController : BaseController, IRunner
              {
                  [HttpGet] public void Run() { }
                  internal void Hidden() { }
              }",
            filePath: "/repo/src/Api/Controller.cs");

        var index = CreateIndex();
        var first = await index.BuildAsync(solution, "test-version", CancellationToken.None, "/repo");
        var second = await index.BuildAsync(solution, "test-version", CancellationToken.None, "/repo");

        Assert.Equal(first.Symbols.Select(s => s.SymbolId), second.Symbols.Select(s => s.SymbolId));
        Assert.Equal(
            first.Namespaces.SelectMany(project => project.Namespaces).Select(ns => $"{ns.Name}:{string.Join(",", ns.TopTypes.Select(t => t.Name))}"),
            second.Namespaces.SelectMany(project => project.Namespaces).Select(ns => $"{ns.Name}:{string.Join(",", ns.TopTypes.Select(t => t.Name))}"));

        var type = first.Symbols.Single(symbol => symbol.Kind == "NamedType" && symbol.Name == "OrdersController");
        Assert.Equal("public", type.Visibility);
        Assert.Equal("BaseController", type.BaseType);
        Assert.Contains("IRunner", type.Interfaces);
        Assert.Equal(1, type.DeclaredPublicMethodCount);
        Assert.Equal(1, type.PubliclyReachableMethodCount);
        Assert.Equal(2, type.TotalMethodCount);
        Assert.Contains("Authorize", type.Attributes);
        Assert.Equal("src/Api/Controller.cs", type.RelativePath);

        var method = first.Symbols.Single(symbol => symbol.Kind == "PublicMethod" && symbol.Name == "Run" && symbol.ContainingType == "OrdersController");
        Assert.Equal("public", method.Visibility);
        Assert.Equal("src/Api/Controller.cs", method.RelativePath);

        var apiNamespace = first.Namespaces.Single(n => n.ProjectName == "Api").Namespaces.Single(n => n.Name == "Api.Controllers");
        Assert.True(apiNamespace.TopTypes.Count <= 10);
        Assert.Equal(apiNamespace.TopTypes.OrderByDescending(t => t.DeclaredPublicMethodCount)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToArray(), apiNamespace.TopTypes.ToArray());
    }

    private static SymbolIndex CreateIndex()
    {
        var fileSystem = new PhysicalFileSystem();
        var hashService = new DocumentHashService(fileSystem);
        var cache = new DocumentCache(new InMemoryCacheStore());
        return new SymbolIndex(new DocumentFilter(), hashService, cache, maxDegreeOfParallelism: 2);
    }
}
