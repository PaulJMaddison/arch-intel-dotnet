using ArchIntel.Analysis;
using ArchIntel.Logging;
using ArchIntel.Reports;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

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
        var outputOption = new Option<string?>(
            "--out",
            "Output directory for reports (defaults to ./.archintel in the solution directory).");
        var configOption = new Option<string?>("--config", "Path to the analysis config JSON file.");
        var formatOption = new Option<string?>(
            "--format",
            () => "both",
            "Report format: json, md, or both.");
        var openOption = new Option<bool>("--open", "Open the report output directory after completion.");
        var verboseOption = new Option<bool>("--verbose", "Print full MSBuild workspace diagnostics.");
        var failOnLoadIssuesOption = new Option<bool?>(
            "--fail-on-load-issues",
            "Fail if MSBuild reports fatal load issues.");
        var strictOption = new Option<bool>("--strict", "Enable strict mode (CI-grade exit codes).");
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
            failOnLoadIssuesOption,
            strictOption
        };
        scanCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, strict, verbose, openOutput) =>
        {
            await CliExecutor.RunReportAsync(
                logger,
                new SolutionLoader(logger),
                new ReportWriter(),
                solution,
                output,
                configPath,
                format,
                failOnLoadIssues,
                strict,
                verbose,
                "scan",
                null,
                openOutput,
                CancellationToken.None);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, strictOption, verboseOption, openOption);

        var passportCommand = new Command("passport", "Generate an architecture passport.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption
        };
        passportCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, strict, verbose, openOutput) =>
        {
            await CliExecutor.RunReportAsync(
                logger,
                new SolutionLoader(logger),
                new ReportWriter(),
                solution,
                output,
                configPath,
                format,
                failOnLoadIssues,
                strict,
                verbose,
                "passport",
                null,
                openOutput,
                CancellationToken.None);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, strictOption, verboseOption, openOption);

        var impactCommand = new Command("impact", "Analyze impact for a specific symbol.")
        {
            solutionOption,
            symbolOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption
        };
        impactCommand.SetHandler(async (solution, symbol, output, configPath, format, failOnLoadIssues, strict, verbose, openOutput) =>
        {
            await CliExecutor.RunReportAsync(
                logger,
                new SolutionLoader(logger),
                new ReportWriter(),
                solution,
                output,
                configPath,
                format,
                failOnLoadIssues,
                strict,
                verbose,
                "impact",
                symbol,
                openOutput,
                CancellationToken.None);
        }, solutionOption, symbolOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, strictOption, verboseOption, openOption);

        var projectGraphCommand = new Command("project-graph", "Generate a project dependency graph.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption
        };
        projectGraphCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, strict, verbose, openOutput) =>
        {
            await CliExecutor.RunReportAsync(
                logger,
                new SolutionLoader(logger),
                new ReportWriter(),
                solution,
                output,
                configPath,
                format,
                failOnLoadIssues,
                strict,
                verbose,
                "project_graph",
                null,
                openOutput,
                CancellationToken.None);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, strictOption, verboseOption, openOption);

        var violationsCommand = new Command("violations", "Check architecture rules and drift.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption
        };
        violationsCommand.SetHandler(async (solution, output, configPath, format, failOnLoadIssues, strict, verbose, openOutput) =>
        {
            await CliExecutor.RunReportAsync(
                logger,
                new SolutionLoader(logger),
                new ReportWriter(),
                solution,
                output,
                configPath,
                format,
                failOnLoadIssues,
                strict,
                verbose,
                "violations",
                null,
                openOutput,
                CancellationToken.None);
        }, solutionOption, outputOption, configOption, formatOption, failOnLoadIssuesOption, strictOption, verboseOption, openOption);

        var root = new RootCommand(
            """
            ArchIntel CLI

            Examples:
              arch scan --solution ./MySolution.sln
              arch scan --solution ./MySolution.sln --format both
              arch impact --solution ./MySolution.sln --symbol My.Namespace.Type --format json
            """)
        {
            scanCommand,
            passportCommand,
            impactCommand,
            projectGraphCommand,
            violationsCommand
        };

        // Global options apply to all subcommands.
        root.AddGlobalOption(openOption);
        root.AddGlobalOption(verboseOption);

        var parser = new CommandLineBuilder(root)
            .UseDefaults() // includes --help, --version, and standard middleware
            .Build();

        try
        {
            // IMPORTANT: invoke the parser we built with UseDefaults(), to avoid duplicate middleware/options.
            return await parser.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            SafeLog.Error(logger, "Unexpected error: {Message}", SafeLog.SanitizeValue(ex.Message));
            return ExitCodes.UnexpectedError;
        }
    }
}
