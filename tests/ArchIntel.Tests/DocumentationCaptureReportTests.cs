using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.IO;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class DocumentationCaptureReportTests
{
    [Fact]
    public void ExtractHeadings_ReturnsFirstHeadings()
    {
        var content = """
            # Title
            Intro text.
            ## Overview
            ### Details ###
            Not a heading
            #### Next Step
            """;

        var headings = DocumentationCaptureReport.ExtractHeadings(content, 3);

        Assert.Equal(new[] { "Title", "Overview", "Details" }, headings);
    }

    [Fact]
    public async Task CreateAsync_DiscoversDocsDeterministically_AndCapturesSummaryMetadata()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = temp.Path;
        Directory.CreateDirectory(Path.Combine(repoRoot, "docs", "adr"));
        Directory.CreateDirectory(Path.Combine(repoRoot, "docs", "guide"));
        Directory.CreateDirectory(Path.Combine(repoRoot, "notes"));

        await File.WriteAllTextAsync(Path.Combine(repoRoot, "README.md"), "# Root Readme\nWelcome.");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "CHANGELOG.md"), "# Changelog\n- init");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "docs", "intro.md"), "# Intro\nHello docs.");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "docs", "guide", "setup.md"), "# Setup\nSteps.");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "docs", "adr", "0001-record.md"), "# ADR 0001\nDecision.");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "docs", "adr", "decision-log.txt"), "Decision log text file.");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "notes", "system-design.md"), "# System Design\r\nLine two.\r\n");

        var context = CreateContext(repoRoot, includeSnippets: false);

        var first = await DocumentationCaptureReport.CreateAsync(context, new PhysicalFileSystem(), includeSnippets: false, CancellationToken.None);
        var second = await DocumentationCaptureReport.CreateAsync(context, new PhysicalFileSystem(), includeSnippets: false, CancellationToken.None);

        var expectedPaths = new[]
        {
            "CHANGELOG.md",
            "README.md",
            "docs/adr/0001-record.md",
            "docs/adr/decision-log.txt",
            "docs/guide/setup.md",
            "docs/intro.md",
            "notes/system-design.md"
        };

        Assert.Equal(expectedPaths, first.Documents.Select(document => document.RelativePath).ToArray());
        Assert.Equal(first.Documents.Select(document => document.RelativePath), second.Documents.Select(document => document.RelativePath));
        Assert.Equal(first.Documents.Select(document => document.ContentHash), second.Documents.Select(document => document.ContentHash));

        Assert.Equal(first.Documents.Count, first.Summary.AnalyzedDocuments);
        Assert.False(first.Summary.IncludeSnippets);

        var readme = first.Documents.Single(document => document.RelativePath == "README.md");
        Assert.Equal("Root Readme", readme.Title);
        Assert.True(readme.WordCount > 0);
        Assert.Null(readme.Snippet);

        var adrTextFile = first.Documents.Single(document => document.RelativePath == "docs/adr/decision-log.txt");
        Assert.Equal("decision-log", adrTextFile.Title);

        var designDoc = first.Documents.Single(document => document.RelativePath == "notes/system-design.md");
        var expectedDesignHash = new DocumentHashService(new PhysicalFileSystem()).GetContentHash("# System Design\nLine two.\n");
        Assert.Equal(expectedDesignHash, designDoc.ContentHash);
    }

    private static AnalysisContext CreateContext(string repoRoot, bool includeSnippets)
    {
        var workspace = new AdhocWorkspace();
        var config = new AnalysisConfig
        {
            OutputDir = Path.Combine(repoRoot, "out"),
            CacheDir = Path.Combine(repoRoot, "cache"),
            IncludeDocSnippets = includeSnippets,
            MaxDegreeOfParallelism = 1
        };

        return new AnalysisContext(
            Path.Combine(repoRoot, "arch.sln"),
            repoRoot,
            workspace.CurrentSolution,
            config,
            NullLogger.Instance,
            0,
            0,
            loadDiagnostics: Array.Empty<LoadDiagnostic>());
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
