using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Diagnostics;
using ArchIntel.Analysis;
using ArchIntel.Configuration;
using ArchIntel.Logging;
using ArchIntel.Reports;
using Microsoft.Extensions.Logging;

namespace ArchIntel;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        }));
        var logger = loggerFactory.CreateLogger("arch");

        var solutionOption = new Option<string>("--solution")
        {
            IsRequired = true,
            Description = "Path to the solution file or directory."
        };
        var outputOption = new Option<string?>("--out", "Output directory for reports.");
        var configOption = new Option<string?>("--config", "Path to the analysis config JSON file.");
        var formatOption = new Option<string?>("--format", "Report format: json, md, or both.");
        var openOption = new Option<bool>("--open", "Open the report output directory after completion.");
        var failOnLoadIssuesOption = new Option<bool?>(
            "--fail-on-load-issues",
            "Fail if MSBuild reports fatal load issues.");
        var symbolOption = new Option<string>("--symbol")
        {
            IsRequired = true,
            Description = "Fully-qualified symbol name for impact analysis."
        };

        var scanCommand = new Command("scan", "Scan a solution for architecture insights.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption
        };
        scanCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, failOnLoadIssues, "scan", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, openOption);

        var passportCommand = new Command("passport", "Generate an architecture passport.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption
        };
        passportCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, failOnLoadIssues, "passport", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, openOption);

        var impactCommand = new Command("impact", "Analyze impact for a specific symbol.")
        {
            solutionOption,
            symbolOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption
        };
        impactCommand.SetHandler(async (solution, symbol, output, configPath, format, failOnLoadIssues, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, failOnLoadIssues, "impact", symbol, openOutput);
        }, solutionOption, symbolOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, openOption);

        var projectGraphCommand = new Command("project-graph", "Generate a project dependency graph.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption
        };
        projectGraphCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, failOnLoadIssues, "project_graph", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, openOption);

        var violationsCommand = new Command("violations", "Check architecture rules and drift.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption
        };
        violationsCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, failOnLoadIssues, "violations", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, openOption);

        var root = new RootCommand("ArchIntel CLI")
        {
            scanCommand,
            passportCommand,
            impactCommand,
            projectGraphCommand,
            violationsCommand
        };

        root.AddGlobalOption(openOption);

        root.SetHandler(static (InvocationContext context) =>
        {
            var helpContext = new HelpContext(
                context.HelpBuilder,
                context.ParseResult.CommandResult.Command,
                Console.Out,
                context.ParseResult);
            context.HelpBuilder.Write(helpContext);
        });

        var parser = new CommandLineBuilder(root)
            .UseDefaults()
            .Build();

        return await parser.InvokeAsync(args);
    }

    private static async Task RunReportAsync(
        ILogger logger,
        string solution,
        string? output,
        string? configPath,
        string? format,
        bool? failOnLoadIssues,
        string reportKind,
        string? symbol,
        bool openOutput)
    {
        var config = AnalysisConfig.Load(configPath);
        var mergedConfig = MergeConfig(config, output, failOnLoadIssues);
        var pipelineTimer = new PipelineTimer();
        var solutionLoader = new SolutionLoader(logger);
        var reportFormat = ParseFormat(format);

        using var cancellationSource = new CancellationTokenSource();
        ConsoleCancelEventHandler? handler = (_, args) =>
        {
            args.Cancel = true;
            cancellationSource.Cancel();
        };

        Console.CancelKeyPress += handler;
        try
        {
            var loadResult = await pipelineTimer.TimeLoadSolutionAsync(
                () => solutionLoader.LoadAsync(solution, mergedConfig.FailOnLoadIssues, cancellationSource.Token));
            var context = new AnalysisContext(
                loadResult.SolutionPath,
                loadResult.RepoRootPath,
                loadResult.Solution,
                mergedConfig,
                logger,
                pipelineTimer: pipelineTimer,
                loadDiagnostics: loadResult.LoadDiagnostics);

            SafeLog.Info(logger, "Generating {ReportKind} report for {Solution}.", reportKind, SafeLog.SanitizePath(solution));

            var writer = new ReportWriter();
            await writer.WriteAsync(context, reportKind, symbol, reportFormat, cancellationSource.Token);

            SafeLog.Info(logger, "Report written to {OutputDir}.", SafeLog.SanitizePath(context.OutputDir));

            if (string.Equals(reportKind, "scan", StringComparison.OrdinalIgnoreCase))
            {
                LogLoadDiagnosticsSummary(logger, context.LoadDiagnostics);
            }

            if (openOutput)
            {
                OpenOutputDirectory(logger, context.OutputDir);
            }
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
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

    private static AnalysisConfig MergeConfig(AnalysisConfig config, string? output, bool? failOnLoadIssues)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            if (failOnLoadIssues is null)
            {
                return config;
            }

            return new AnalysisConfig
            {
                IncludeGlobs = config.IncludeGlobs,
                ExcludeGlobs = config.ExcludeGlobs,
                OutputDir = config.OutputDir,
                CacheDir = config.CacheDir,
                MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
                FailOnLoadIssues = failOnLoadIssues.Value,
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
            ArchitectureRules = config.ArchitectureRules
        };
    }

    private static void LogLoadDiagnosticsSummary(ILogger logger, IReadOnlyList<LoadDiagnostic> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            SafeLog.Info(logger, "Load issues: none.");
            return;
        }

        const int maxMessages = 3;
        var messageList = diagnostics
            .Take(maxMessages)
            .Select(diagnostic => SafeLog.SanitizeValue(diagnostic.Message))
            .ToList();

        var summary = string.Join(" | ", messageList);
        SafeLog.Warn(
            logger,
            "Load issues: {Count}. Top {TopCount}: {Summary}",
            diagnostics.Count,
            messageList.Count,
            summary);
    }

    private static ReportFormat ParseFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            return ReportFormat.Json;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "json" => ReportFormat.Json,
            "md" => ReportFormat.Markdown,
            "both" => ReportFormat.Both,
            _ => throw new ArgumentException("Unsupported format. Use json, md, or both.")
        };
    }
}
