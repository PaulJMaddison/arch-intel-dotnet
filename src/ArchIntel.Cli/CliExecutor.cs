using System.Diagnostics;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.Logging;
using ArchIntel.Reports;
using Microsoft.Extensions.Logging;

namespace ArchIntel;

internal static class CliExecutor
{
    public static async Task<int> RunReportAsync(
        ILogger logger,
        ISolutionLoader solutionLoader,
        IReportWriter reportWriter,
        string solution,
        string? output,
        string? configPath,
        string? format,
        bool? failOnLoadIssues,
        bool strict,
        bool verbose,
        string reportKind,
        string? symbol,
        bool openOutput,
        CancellationToken cancellationToken)
    {
        var config = AnalysisConfig.Load(configPath);
        var mergedConfig = MergeConfig(config, output, failOnLoadIssues, strict);
        var strictSettings = ResolveStrictSettings(mergedConfig.Strict, strict);
        var pipelineTimer = new PipelineTimer();
        var reportFormat = ParseFormat(format);
        var failOnLoadIssuesEffective = strict ? strictSettings.FailOnLoadIssues : mergedConfig.FailOnLoadIssues;

        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler? handler = (_, args) =>
        {
            args.Cancel = true;
            cancellationSource.Cancel();
        };

        Console.CancelKeyPress += handler;
        try
        {
            var loadResult = await pipelineTimer.TimeLoadSolutionAsync(
                () => solutionLoader.LoadAsync(solution, failOnLoadIssuesEffective, verbose, cancellationSource.Token));

            var context = new AnalysisContext(
                loadResult.SolutionPath,
                loadResult.RepoRootPath,
                loadResult.Solution,
                mergedConfig,
                logger,
                loadResult.ProjectCount,
                loadResult.FailedProjectCount,
                pipelineTimer: pipelineTimer,
                loadDiagnostics: loadResult.LoadDiagnostics);

            SafeLog.Info(logger, "Generating {ReportKind} report for {Solution}.", reportKind, SafeLog.SanitizePath(solution));

            var reportOutcome = await reportWriter.WriteAsync(context, reportKind, symbol, reportFormat, cancellationSource.Token);

            SafeLog.Info(logger, "Report written to {OutputDir}.", SafeLog.SanitizePath(context.OutputDir));

            if (openOutput)
            {
                OpenOutputDirectory(logger, context.OutputDir);
            }

            return DetermineExitCode(
                logger,
                reportKind,
                loadResult.LoadDiagnostics,
                loadResult.ProjectCount,
                reportOutcome,
                strict,
                strictSettings,
                failOnLoadIssuesEffective);
        }
        catch (SolutionLoadException ex)
        {
            SafeLog.Error(logger, "Solution load failed: {Message}", SafeLog.SanitizeValue(ex.Message));
            return ExitCodes.FatalLoadFailure;
        }
        catch (Exception ex)
        {
            SafeLog.Error(logger, "Unexpected error: {Message}", SafeLog.SanitizeValue(ex.Message));
            return ExitCodes.UnexpectedError;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static int DetermineExitCode(
        ILogger logger,
        string reportKind,
        IReadOnlyList<LoadDiagnostic> loadDiagnostics,
        int projectCount,
        ReportOutcome reportOutcome,
        bool strict,
        StrictSettings strictSettings,
        bool failOnLoadIssues)
    {
        var loadIssueCount = loadDiagnostics.Count;
        var fatalLoadIssueCount = loadDiagnostics.Count(diagnostic => diagnostic.IsFatal);
        var violationCount = reportOutcome.ViolationCount ?? 0;

        if (projectCount == 0)
        {
            SafeLog.Error(
                logger,
                "No projects were loaded from the solution. Verify the solution builds locally and try again.");
            return ExitCodes.FatalLoadFailure;
        }

        if (strict)
        {
            var gatedViolations = strictSettings.FailOnViolations ? violationCount : 0;
            if (fatalLoadIssueCount > 0 || gatedViolations > 0)
            {
                SafeLog.Warn(
                    logger,
                    "STRICT MODE: failing due to {LoadIssueCount} fatal load issues and {ViolationCount} violations. See scan_summary.json.",
                    fatalLoadIssueCount,
                    gatedViolations);
                return ExitCodes.StrictModeFailure;
            }

            return ExitCodes.Success;
        }

        if (failOnLoadIssues && fatalLoadIssueCount > 0)
        {
            SafeLog.Warn(
                logger,
                "Failing due to {Count} fatal load issues (see scan_summary.json).",
                fatalLoadIssueCount);
            return ExitCodes.FatalLoadFailure;
        }

        if (loadIssueCount > 0)
        {
            SafeLog.Warn(
                logger,
                "Completed with {Count} load issues (see scan_summary.json).",
                loadIssueCount);
        }

        return ExitCodes.Success;
    }

    private static void OpenOutputDirectory(ILogger logger, string outputDir)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SafeLog.Warn(logger, "Unable to open output directory: {Message}", SafeLog.SanitizeValue(ex.Message));
        }
    }

    private static StrictSettings ResolveStrictSettings(StrictModeConfig strictConfig, bool strict)
    {
        if (!strict)
        {
            return new StrictSettings(false, false);
        }

        return new StrictSettings(
            strictConfig.FailOnLoadIssues ?? true,
            strictConfig.FailOnViolations ?? true);
    }

    private static AnalysisConfig MergeConfig(AnalysisConfig config, string? output, bool? failOnLoadIssues, bool strict)
    {
        var strictConfig = config.Strict ?? new StrictModeConfig();
        var mergedStrict = strict
            ? new StrictModeConfig
            {
                FailOnLoadIssues = strictConfig.FailOnLoadIssues ?? true,
                FailOnViolations = strictConfig.FailOnViolations ?? true
            }
            : strictConfig;

        if (string.IsNullOrWhiteSpace(output))
        {
            if (failOnLoadIssues is null)
            {
                return new AnalysisConfig
                {
                    IncludeGlobs = config.IncludeGlobs,
                    ExcludeGlobs = config.ExcludeGlobs,
                    OutputDir = config.OutputDir,
                    CacheDir = config.CacheDir,
                    MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                    FailOnLoadIssues = config.FailOnLoadIssues,
                    Strict = mergedStrict,
                    ArchitectureRules = config.ArchitectureRules
                };
            }

            return new AnalysisConfig
            {
                IncludeGlobs = config.IncludeGlobs,
                ExcludeGlobs = config.ExcludeGlobs,
                OutputDir = config.OutputDir,
                CacheDir = config.CacheDir,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                FailOnLoadIssues = failOnLoadIssues.Value,
                Strict = mergedStrict,
                ArchitectureRules = config.ArchitectureRules
            };
        }

        return new AnalysisConfig
        {
            IncludeGlobs = config.IncludeGlobs,
            ExcludeGlobs = config.ExcludeGlobs,
            OutputDir = output,
            CacheDir = config.CacheDir,
            MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
            FailOnLoadIssues = failOnLoadIssues ?? config.FailOnLoadIssues,
            Strict = mergedStrict,
            ArchitectureRules = config.ArchitectureRules
        };
    }

    private static ReportFormat ParseFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return ReportFormat.Both;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "json" => ReportFormat.Json,
            "md" => ReportFormat.Markdown,
            "both" => ReportFormat.Both,
            _ => throw new ArgumentException("Unsupported format. Use json, md, or both.")
        };
    }

    private sealed record StrictSettings(bool FailOnLoadIssues, bool FailOnViolations);
}
