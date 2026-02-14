using ArchIntel.Analysis;
using ArchIntel.IO;
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
        var includeDocSnippetsOption = new Option<bool>(
            "--include-doc-snippets",
            "Include the first 2000 characters of documentation files in docs.json outputs.");
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
            strictOption,
            includeDocSnippetsOption
        };
        async Task RunReportAsync(string reportKind, InvocationContext context)
        {
            var solution = context.ParseResult.GetValueForOption(solutionOption);
            var output = context.ParseResult.GetValueForOption(outputOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var format = context.ParseResult.GetValueForOption(formatOption);
            var failOnLoadIssues = context.ParseResult.GetValueForOption(failOnLoadIssuesOption);
            var strict = context.ParseResult.GetValueForOption(strictOption);
            var includeDocSnippets = context.ParseResult.GetValueForOption(includeDocSnippetsOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var openOutput = context.ParseResult.GetValueForOption(openOption);
            var symbol = string.Equals(reportKind, "impact", StringComparison.OrdinalIgnoreCase)
                ? context.ParseResult.GetValueForOption(symbolOption)
                : null;

            await CliExecutor.RunReportAsync(
                logger,
                new SolutionLoader(logger),
                new ReportWriter(),
                solution!,
                output,
                configPath,
                format,
                failOnLoadIssues,
                strict,
                includeDocSnippets,
                verbose,
                reportKind,
                symbol,
                openOutput,
                BuildCliInvocation(context.ParseResult, solution, output, configPath),
                CancellationToken.None);
        }

        scanCommand.SetHandler(async context => await RunReportAsync("scan", context));

        var passportCommand = new Command("passport", "Generate an architecture passport.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption,
            includeDocSnippetsOption
        };
        passportCommand.SetHandler(async context => await RunReportAsync("passport", context));

        var impactCommand = new Command("impact", "Analyze impact for a specific symbol.")
        {
            solutionOption,
            symbolOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption,
            includeDocSnippetsOption
        };
        impactCommand.SetHandler(async context => await RunReportAsync("impact", context));

        var projectGraphCommand = new Command("project-graph", "Generate a project dependency graph.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption,
            includeDocSnippetsOption
        };
        projectGraphCommand.SetHandler(async context => await RunReportAsync("project_graph", context));

        var violationsCommand = new Command("violations", "Check architecture rules and drift.")
        {
            solutionOption,
            outputOption,
            configOption,
            formatOption,
            failOnLoadIssuesOption,
            strictOption,
            includeDocSnippetsOption
        };
        violationsCommand.SetHandler(async context => await RunReportAsync("violations", context));

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

    private static string BuildCliInvocation(ParseResult parseResult, string? solutionPath, string? outputPath, string? configPath)
    {
        var cwd = Directory.GetCurrentDirectory();
        var tokens = parseResult.Tokens
            .Select(token => SanitizeInvocationToken(token.Value, cwd, solutionPath, outputPath, configPath))
            .ToArray();
        return tokens.Length == 0 ? "arch" : $"arch {string.Join(' ', tokens)}";
    }

    private static string SanitizeInvocationToken(string token, string currentDirectory, params string?[] candidatePaths)
    {
        foreach (var candidatePath in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                continue;
            }

            var normalizedCandidate = Path.GetFullPath(candidatePath);
            if (Path.IsPathRooted(token) && PathsEqual(token, normalizedCandidate))
            {
                return CanonicalPath.Normalize(normalizedCandidate, currentDirectory);
            }

            if (string.Equals(token, candidatePath, StringComparison.Ordinal))
            {
                return CanonicalPath.Normalize(normalizedCandidate, currentDirectory);
            }
        }

        return token;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

}
