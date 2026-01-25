using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ArchitecturePassportGeneratorTests
{
    [Fact]
    public async Task BuildAsync_ProducesStableOutput()
    {
        var solution = CreateSolution();

        var output1 = await BuildPassportAsync(solution);
        var output2 = await BuildPassportAsync(solution);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public async Task BuildAsync_DetectsTechSignals()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;
        var projectId = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "MyApp.Api", "MyApp.Api", LanguageNames.CSharp, filePath: "/repo/src/MyApp.Api/MyApp.Api.csproj"));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Controllers.cs",
            @"namespace MyApp.Controllers; public class OrdersController { }",
            filePath: "/repo/src/MyApp.Api/Controllers.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Data.cs",
            @"namespace MyApp.Data; public class AppDbContext { }",
            filePath: "/repo/src/MyApp.Api/Data.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(projectId),
            "Logging.cs",
            @"namespace Serilog; public class Logger { }",
            filePath: "/repo/src/MyApp.Api/Logging.cs");

        var output = await BuildPassportAsync(solution);

        Assert.Contains("ASP.NET Core", output, StringComparison.Ordinal);
        Assert.Contains("Entity Framework Core", output, StringComparison.Ordinal);
        Assert.Contains("Serilog", output, StringComparison.Ordinal);
    }

    private static async Task<string> BuildPassportAsync(Solution solution)
    {
        var context = new AnalysisContext(
            "/repo/arch.sln",
            "/repo",
            solution,
            new AnalysisConfig { MaxDegreeOfParallelism = 1 },
            NullLogger.Instance);

        var generator = new ArchitecturePassportGenerator(new PhysicalFileSystem(), new InMemoryCacheStore());
        return await generator.BuildAsync(context, CancellationToken.None);
    }

    private static Solution CreateSolution()
    {
        var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var appId = ProjectId.CreateNewId();
        var dataId = ProjectId.CreateNewId();

        solution = solution.AddProject(ProjectInfo.Create(appId, VersionStamp.Create(), "App.Api", "App.Api", LanguageNames.CSharp, filePath: "/repo/src/App.Api/App.Api.csproj"));
        solution = solution.AddProject(ProjectInfo.Create(dataId, VersionStamp.Create(), "App.Data", "App.Data", LanguageNames.CSharp, filePath: "/repo/src/App.Data/App.Data.csproj"));
        solution = solution.AddProjectReference(appId, new ProjectReference(dataId));

        solution = solution.AddDocument(
            DocumentId.CreateNewId(appId),
            "Startup.cs",
            @"namespace App.Api; public class Startup { public void ConfigureServices() { } }",
            filePath: "/repo/src/App.Api/Startup.cs");

        solution = solution.AddDocument(
            DocumentId.CreateNewId(dataId),
            "Context.cs",
            @"namespace App.Data; public class AppDbContext { }",
            filePath: "/repo/src/App.Data/Context.cs");

        return solution;
    }
}
