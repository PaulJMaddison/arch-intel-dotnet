using ArchIntel.Analysis;
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
