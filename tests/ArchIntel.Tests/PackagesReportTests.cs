using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class PackagesReportTests
{
    [Fact]
    public async Task PackagesReport_WritesPackageAndFrameworkReferences()
    {
        using var temp = new TemporaryDirectory();
        var projectPath = Path.Combine(temp.Path, "Sample", "Sample.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
        await File.WriteAllTextAsync(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net7.0</TargetFrameworks>
                <NpgsqlVersion>8.0.0-preview</NpgsqlVersion>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.1" />
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="$(NpgsqlVersion)" />
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """);

        var solution = CreateSolution(projectPath);
        var context = CreateContext(solution, temp.Path);
        var outputDir = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(outputDir);

        await PackagesReport.WriteAsync(context, new PhysicalFileSystem(), outputDir, CancellationToken.None);

        var json = await File.ReadAllTextAsync(Path.Combine(outputDir, "packages.json"));
        using var document = JsonDocument.Parse(json);
        var projects = document.RootElement.GetProperty("Projects");

        Assert.Equal(1, projects.GetArrayLength());
        var project = projects[0];
        Assert.Equal("Sample", project.GetProperty("ProjectName").GetString());

        Assert.Equal(
            new[] { "net7.0", "net8.0" },
            project.GetProperty("TargetFrameworks")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray());

        var packageReferences = project.GetProperty("PackageReferences");
        Assert.Collection(
            packageReferences.EnumerateArray(),
            reference =>
            {
                Assert.Equal("Microsoft.EntityFrameworkCore", reference.GetProperty("Id").GetString());
                Assert.Equal("8.0.1", reference.GetProperty("Version").GetString());
            },
            reference =>
            {
                Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", reference.GetProperty("Id").GetString());
                Assert.Equal("$(NpgsqlVersion)", reference.GetProperty("Version").GetString());
            });

        var frameworkReferences = project.GetProperty("FrameworkReferences");
        Assert.Collection(
            frameworkReferences.EnumerateArray(),
            reference => Assert.Equal("Microsoft.AspNetCore.App", reference.GetProperty("Id").GetString()));
    }

    private static Solution CreateSolution(string projectPath)
    {
        var workspace = new AdhocWorkspace();
        var solutionId = SolutionId.CreateNewId();
        var projectId = ProjectId.CreateNewId();

        var project = ProjectInfo.Create(
            projectId,
            VersionStamp.Create(),
            "Sample",
            "Sample",
            LanguageNames.CSharp,
            filePath: projectPath);

        return workspace.AddSolution(
            SolutionInfo.Create(solutionId, VersionStamp.Create(), projects: new[] { project }));
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
            Path.Combine(outputDir, "arch.sln"),
            outputDir,
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
