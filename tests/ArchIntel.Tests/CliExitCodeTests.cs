using ArchIntel.Analysis;
using ArchIntel.Reports;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArchIntel.Tests;

public sealed class CliExitCodeTests
{
    [Fact]
    public async Task DefaultMode_ExitsZero_WithNonFatalLoadDiagnostics()
    {
        var loadResult = CreateLoadResult(new[]
        {
            new LoadDiagnostic("Warning", "NU1605 Detected package downgrade.", false)
        });
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(null)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            false,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(writer.WasCalled);
    }

    [Fact]
    public async Task StrictMode_ExitsTwo_WhenFatalLoadDiagnosticsExist()
    {
        var loadResult = CreateLoadResult(new[]
        {
            new LoadDiagnostic("Failure", "The SDK 'Microsoft.NET.Sdk' specified could not be found.", true)
        });
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(null)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            true,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.StrictModeFailure, exitCode);
        Assert.True(writer.WasCalled);
    }

    [Fact]
    public async Task StrictMode_ExitsZero_WhenOnlyNonFatalLoadDiagnosticsExist()
    {
        var loadResult = CreateLoadResult(new[]
        {
            new LoadDiagnostic("Warning", "NU1605 Detected package downgrade.", false)
        });
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(null)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            true,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.True(writer.WasCalled);
    }

    [Fact]
    public async Task StrictMode_ExitsTwo_WhenViolationsExist()
    {
        var loadResult = CreateLoadResult(Array.Empty<LoadDiagnostic>());
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(2)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            true,
            false,
            "violations",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.StrictModeFailure, exitCode);
        Assert.True(writer.WasCalled);
    }

    [Fact]
    public async Task StrictMode_ExitsOne_OnFatalLoadFailure()
    {
        var loader = new FakeSolutionLoader(_ => throw new SolutionLoadException("Failed to load."));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(null)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            true,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.FatalLoadFailure, exitCode);
        Assert.False(writer.WasCalled);
    }

    [Fact]
    public async Task CrashPath_ExitsThree_OnUnexpectedException()
    {
        var loadResult = CreateLoadResult(Array.Empty<LoadDiagnostic>());
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => throw new InvalidOperationException("boom"));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            false,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.UnexpectedError, exitCode);
        Assert.True(writer.WasCalled);
    }

    [Fact]
    public async Task DefaultMode_ExitsOne_WhenFailOnLoadIssuesAndFatalDiagnosticsExist()
    {
        var loadResult = CreateLoadResult(new[]
        {
            new LoadDiagnostic("Failure", "The SDK 'Microsoft.NET.Sdk' specified could not be found.", true)
        });
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(null)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            true,
            false,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.FatalLoadFailure, exitCode);
        Assert.True(writer.WasCalled);
    }

    [Fact]
    public async Task DefaultMode_ExitsOne_WhenNoProjectsLoaded()
    {
        var loadResult = CreateLoadResult(Array.Empty<LoadDiagnostic>()) with { ProjectCount = 0 };
        var loader = new FakeSolutionLoader(_ => Task.FromResult(loadResult));
        var writer = new FakeReportWriter((_, _, _, _, _) => Task.FromResult(new ReportOutcome(null)));

        var exitCode = await CliExecutor.RunReportAsync(
            NullLogger.Instance,
            loader,
            writer,
            "test.sln",
            null,
            null,
            null,
            null,
            false,
            false,
            "scan",
            null,
            false,
            CancellationToken.None);

        Assert.Equal(ExitCodes.FatalLoadFailure, exitCode);
        Assert.True(writer.WasCalled);
    }

    private static SolutionLoadResult CreateLoadResult(IReadOnlyList<LoadDiagnostic> diagnostics)
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution.AddProject("Test", "Test", LanguageNames.CSharp).Solution;
        return new SolutionLoadResult("test.sln", "repo", solution, diagnostics, solution.Projects.Count(), 0);
    }

    private sealed class FakeSolutionLoader : ISolutionLoader
    {
        private readonly Func<string, Task<SolutionLoadResult>> _handler;

        public FakeSolutionLoader(Func<string, Task<SolutionLoadResult>> handler)
        {
            _handler = handler;
        }

        public Task<SolutionLoadResult> LoadAsync(
            string solutionPathOrDirectory,
            bool failOnLoadIssues,
            bool verbose,
            CancellationToken cancellationToken)
        {
            return _handler(solutionPathOrDirectory);
        }
    }

    private sealed class FakeReportWriter : IReportWriter
    {
        private readonly Func<AnalysisContext, string, string?, ReportFormat, CancellationToken, Task<ReportOutcome>> _handler;

        public FakeReportWriter(Func<AnalysisContext, string, string?, ReportFormat, CancellationToken, Task<ReportOutcome>> handler)
        {
            _handler = handler;
        }

        public bool WasCalled { get; private set; }

        public Task<ReportOutcome> WriteAsync(
            AnalysisContext context,
            string reportKind,
            string? symbol,
            ReportFormat format,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return _handler(context, reportKind, symbol, format, cancellationToken);
        }
    }
}
