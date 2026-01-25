using System.CommandLine;
using System.CommandLine.Builder;
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
            formatOption
        };
        scanCommand.SetHandler(async (solution, output, configPath, format, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, "scan", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, openOption);

        var passportCommand = new Command("passport", "Generate an architecture passport.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption
        };
        passportCommand.SetHandler(async (solution, output, configPath, format, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, "passport", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, openOption);

        var impactCommand = new Command("impact", "Analyze impact for a specific symbol.")
        {
            solutionOption,
            symbolOption,
            outputOption,
            configOption,
            formatOption
        };
        impactCommand.SetHandler(async (solution, symbol, output, configPath, format, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, "impact", symbol, openOutput);
        }, solutionOption, symbolOption, outputOption, configOption, formatOption, openOption);

        var projectGraphCommand = new Command("project-graph", "Generate a project dependency graph.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption
        };
        projectGraphCommand.SetHandler(async (solution, output, configPath, format, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, "project_graph", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, openOption);

        var violationsCommand = new Command("violations", "Check architecture rules and drift.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption
        };
        violationsCommand.SetHandler(async (solution, output, configPath, format, openOutput) =>
        {
            await RunReportAsync(logger, solution, output, configPath, format, "violations", null, openOutput);
        }, solutionOption, outputOption, configOption, formatOption, openOption);

        var root = new RootCommand("ArchIntel CLI")
        {
            scanCommand,
            passportCommand,
            impactCommand,
            projectGraphCommand,
            violationsCommand
        };

        root.AddGlobalOption(openOption);

        var versionOption = new Option<bool>("--version", "Show version information.");
        root.AddOption(versionOption);
        root.SetHandler((bool showVersion, InvocationContext context) =>
        {
            if (showVersion)
            {
                var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
                Console.WriteLine($"arch {version}");
                return;
            }

            context.HelpBuilder.Write(root);
        }, versionOption);

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
        string reportKind,
        string? symbol,
        bool openOutput)
    {
        var config = AnalysisConfig.Load(configPath);
        var mergedConfig = MergeConfig(config, output);
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
                () => solutionLoader.LoadAsync(solution, cancellationSource.Token));
            var context = new AnalysisContext(
                loadResult.SolutionPath,
                loadResult.RepoRootPath,
                loadResult.Solution,
                mergedConfig,
                logger,
                pipelineTimer);

            SafeLog.Info(logger, "Generating {ReportKind} report for {Solution}.", reportKind, SafeLog.SanitizePath(solution));

            var writer = new ReportWriter();
            await writer.WriteAsync(context, reportKind, symbol, reportFormat, cancellationSource.Token);

            SafeLog.Info(logger, "Report written to {OutputDir}.", SafeLog.SanitizePath(context.OutputDir));

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

    private static AnalysisConfig MergeConfig(AnalysisConfig config, string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return config;
        }

        return new AnalysisConfig
        {
            IncludeGlobs = config.IncludeGlobs,
            ExcludeGlobs = config.ExcludeGlobs,
            OutputDir = output,
            CacheDir = config.CacheDir,
            MaxDegreeOfParallelism = config.MaxDegreeOfParallelism,
            ArchitectureRules = config.ArchitectureRules
        };
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
