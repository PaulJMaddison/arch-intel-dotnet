using ArchIntel.Analysis;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class SolutionLoaderTests
{
    [Fact]
    public void ResolveSolutionPath_WhenDirectoryHasSingleSolution_ReturnsSolutionPath()
    {
        using var temp = new TemporaryDirectory();
        var solutionPath = temp.CreateSolution("app.sln", 1);

        var resolved = SolutionLoader.ResolveSolutionPath(temp.Path);

        Assert.Equal(solutionPath, resolved);
    }

    [Fact]
    public void ResolveSolutionPath_WhenMultipleSolutions_ReturnsSolutionWithMostProjects()
    {
        using var temp = new TemporaryDirectory();
        var smaller = temp.CreateSolution("alpha.sln", 1);
        var larger = temp.CreateSolution("gamma.sln", 3);

        var resolved = SolutionLoader.ResolveSolutionPath(temp.Path);

        Assert.Equal(larger, resolved);
        Assert.NotEqual(smaller, resolved);
    }

    [Fact]
    public void ResolveSolutionPath_WhenPathMissing_ThrowsActionableException()
    {
        var missingPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var exception = Assert.Throws<SolutionLoadException>(() => SolutionLoader.ResolveSolutionPath(missingPath));

        Assert.Contains("does not exist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IsFatalWorkspaceDiagnostic_WhenNuGetMessage_ReturnsFalse()
    {
        var diagnostic = CreateDiagnostic(
            WorkspaceDiagnosticKind.Failure,
            "NU1605 Detected package downgrade: Example depends on Something");

        var isFatal = SolutionLoader.IsFatalWorkspaceDiagnostic(diagnostic);

        Assert.False(isFatal);
    }

    [Fact]
    public void IsFatalWorkspaceDiagnostic_WhenSdkMissing_ReturnsTrue()
    {
        var diagnostic = CreateDiagnostic(
            WorkspaceDiagnosticKind.Failure,
            "The SDK 'Microsoft.NET.Sdk' specified could not be found.");

        var isFatal = SolutionLoader.IsFatalWorkspaceDiagnostic(diagnostic);

        Assert.True(isFatal);
    }

    [Fact]
    public void BuildLoadResult_WhenNonFatalDiagnostics_DoesNotThrow()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution.AddProject("Test", "Test", LanguageNames.CSharp);
        var solution = project.Solution; // Fix: Get the Solution from the Project
        var diagnostics = new[]
        {
            CreateDiagnostic(WorkspaceDiagnosticKind.Failure, "NU1202 The package was resolved.")
        };

        var result = SolutionLoader.BuildLoadResult(
            solutionPath: "test.sln",
            repoRootPath: "repo",
            solution: solution,
            diagnostics: diagnostics);

        Assert.Single(result.LoadDiagnostics);
        Assert.False(result.LoadDiagnostics[0].IsFatal);
    }

    [Fact]
    public void BuildLoadResult_SortsDiagnosticsDeterministically()
    {
        using var workspace = new AdhocWorkspace();
        var project = workspace.CurrentSolution.AddProject("Test", "Test", LanguageNames.CSharp);
        var solution = project.Solution;
        var diagnostics = new[]
        {
            CreateDiagnostic(WorkspaceDiagnosticKind.Warning, "Zulu warning"),
            CreateDiagnostic(WorkspaceDiagnosticKind.Failure, "Alpha failure"),
            CreateDiagnostic(WorkspaceDiagnosticKind.Failure, "Bravo failure")
        };

        var result = SolutionLoader.BuildLoadResult(
            solutionPath: "test.sln",
            repoRootPath: "repo",
            solution: solution,
            diagnostics: diagnostics);

        Assert.Equal("Failure", result.LoadDiagnostics[0].Kind);
        Assert.Equal("Alpha failure", result.LoadDiagnostics[0].Message);
        Assert.Equal("Failure", result.LoadDiagnostics[1].Kind);
        Assert.Equal("Bravo failure", result.LoadDiagnostics[1].Message);
        Assert.Equal("Warning", result.LoadDiagnostics[2].Kind);
        Assert.Equal("Zulu warning", result.LoadDiagnostics[2].Message);
    }

    [Fact]
    public void SanitizeDiagnosticMessage_RemovesAbsolutePaths()
    {
        var message = "Failed to load C:\\Users\\Alice\\repo\\App\\App.csproj and /home/bob/repo/Lib/Lib.csproj. " +
                      "MSBuild is at D:\\BuildTools\\MSBuild\\Current.";

        var sanitized = SolutionLoader.SanitizeDiagnosticMessage(message);

        Assert.DoesNotContain("C:\\Users\\Alice", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/bob", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D:\\BuildTools", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<userdir>", sanitized, StringComparison.Ordinal);
        Assert.Contains("<path>", sanitized, StringComparison.Ordinal);
    }

    private static WorkspaceDiagnostic CreateDiagnostic(WorkspaceDiagnosticKind kind, string message)
    {
        var constructor = typeof(WorkspaceDiagnostic).GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(WorkspaceDiagnosticKind), typeof(string) },
            modifiers: null);

        if (constructor is null)
        {
            throw new InvalidOperationException("Unable to locate WorkspaceDiagnostic constructor.");
        }

        return (WorkspaceDiagnostic)constructor.Invoke(new object[] { kind, message });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "archintel-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateSolution(string fileName, int projectCount)
        {
            var solutionPath = System.IO.Path.Combine(Path, fileName);
            var lines = new List<string>
            {
                "Microsoft Visual Studio Solution File, Format Version 12.00",
                "# Visual Studio Version 17"
            };

            for (var i = 0; i < projectCount; i += 1)
            {
                lines.Add($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Project{i}\", \"Project{i}\\\\Project{i}.csproj\", \"{{{Guid.NewGuid():D}}}\"");
                lines.Add("EndProject");
            }

            File.WriteAllLines(solutionPath, lines);
            return solutionPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
