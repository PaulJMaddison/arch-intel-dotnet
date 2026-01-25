using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArchIntel.Tests;

public sealed class ArchitectureRulesEngineTests
{
    [Fact]
    public async Task AnalyzeAsync_DetectsViolations()
    {
        var solution = CreateSolution(builder =>
        {
            var api = builder.AddProject("MyApp.Api", "/repo/src/MyApp.Api/MyApp.Api.csproj");
            var domain = builder.AddProject("MyApp.Domain", "/repo/src/MyApp.Domain/MyApp.Domain.csproj");
            builder.AddReference(api, domain);
        });

        using var tempDir = new TempDirectory();
        var context = CreateContext(solution, tempDir.Path);
        var engine = new ArchitectureRulesEngine(new PhysicalFileSystem());

        var result = await engine.AnalyzeAsync(context, CancellationToken.None);

        Assert.Single(result.Violations);
        Assert.Equal("Presentation", result.Violations[0].FromLayer);
        Assert.Equal("Domain", result.Violations[0].ToLayer);
    }

    [Fact]
    public async Task AnalyzeAsync_AllowsValidDependencies()
    {
        var solution = CreateSolution(builder =>
        {
            var api = builder.AddProject("MyApp.Api", "/repo/src/MyApp.Api/MyApp.Api.csproj");
            var app = builder.AddProject("MyApp.Application", "/repo/src/MyApp.Application/MyApp.Application.csproj");
            var domain = builder.AddProject("MyApp.Domain", "/repo/src/MyApp.Domain/MyApp.Domain.csproj");
            var infra = builder.AddProject("MyApp.Infrastructure", "/repo/src/MyApp.Infrastructure/MyApp.Infrastructure.csproj");
            var domainModels = builder.AddProject("MyApp.Domain.Models", "/repo/src/MyApp.Domain.Models/MyApp.Domain.Models.csproj");

            builder.AddReference(api, app);
            builder.AddReference(app, domain);
            builder.AddReference(infra, domain);
            builder.AddReference(domainModels, domain);
        });

        using var tempDir = new TempDirectory();
        var context = CreateContext(solution, tempDir.Path);
        var engine = new ArchitectureRulesEngine(new PhysicalFileSystem());

        var result = await engine.AnalyzeAsync(context, CancellationToken.None);

        Assert.Empty(result.Violations);
    }

    [Fact]
    public async Task AnalyzeAsync_ReportsDriftAgainstCachedGraph()
    {
        using var tempDir = new TempDirectory();
        var engine = new ArchitectureRulesEngine(new PhysicalFileSystem());

        var initialSolution = CreateSolution(builder =>
        {
            var api = builder.AddProject("MyApp.Api", "/repo/src/MyApp.Api/MyApp.Api.csproj");
            var app = builder.AddProject("MyApp.Application", "/repo/src/MyApp.Application/MyApp.Application.csproj");
            builder.AddReference(api, app);
        });

        var initialContext = CreateContext(initialSolution, tempDir.Path);
        var initialResult = await engine.AnalyzeAsync(initialContext, CancellationToken.None);

        Assert.False(initialResult.Drift.BaselineAvailable);

        var updatedSolution = CreateSolution(builder =>
        {
            var api = builder.AddProject("MyApp.Api", "/repo/src/MyApp.Api/MyApp.Api.csproj");
            var app = builder.AddProject("MyApp.Application", "/repo/src/MyApp.Application/MyApp.Application.csproj");
            var domain = builder.AddProject("MyApp.Domain", "/repo/src/MyApp.Domain/MyApp.Domain.csproj");
            builder.AddReference(api, app);
            builder.AddReference(app, domain);
        });

        var updatedContext = CreateContext(updatedSolution, tempDir.Path);
        var updatedResult = await engine.AnalyzeAsync(updatedContext, CancellationToken.None);

        Assert.True(updatedResult.Drift.BaselineAvailable);
        Assert.Contains(updatedResult.Drift.AddedProjects, project => project.Name == "MyApp.Domain");
        Assert.Contains(updatedResult.Drift.AddedDependencies, edge => edge.FromName == "MyApp.Application" && edge.ToName == "MyApp.Domain");
    }

    private static AnalysisContext CreateContext(Solution solution, string cacheDir)
    {
        var config = new AnalysisConfig
        {
            CacheDir = cacheDir
        };
        var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger("test");

        return new AnalysisContext(
            solution.FilePath ?? "/repo/solution.sln",
            "/repo",
            solution,
            config,
            logger,
            solution.Projects.Count(),
            0);
    }

    private static Solution CreateSolution(Action<SolutionBuilder> configure)
    {
        var workspace = new AdhocWorkspace();
        var builder = new SolutionBuilder(workspace.CurrentSolution);
        configure(builder);
        return builder.Build();
    }

    private sealed class SolutionBuilder
    {
        private Solution _solution;

        public SolutionBuilder(Solution solution)
        {
            _solution = solution;
        }

        public ProjectId AddProject(string name, string path)
        {
            var projectId = ProjectId.CreateNewId();
            _solution = _solution.AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                name,
                name,
                LanguageNames.CSharp,
                filePath: path));
            return projectId;
        }

        public void AddReference(ProjectId from, ProjectId to)
        {
            _solution = _solution.AddProjectReference(from, new ProjectReference(to));
        }

        public Solution Build() => _solution;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"arch-intel-{Guid.NewGuid():N}");
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
