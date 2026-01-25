using System.Text;
using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public enum ReportFormat
{
    Json,
    Markdown,
    Both
}

public sealed class ReportWriter : IReportWriter
{
    private readonly IFileSystem _fileSystem;

    public ReportWriter() : this(new PhysicalFileSystem())
    {
    }

    public ReportWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<ReportOutcome> WriteAsync(AnalysisContext context, string reportKind, string? symbol, ReportFormat format, CancellationToken cancellationToken)
    {
        var outputDirectory = context.OutputDir;
        _fileSystem.CreateDirectory(outputDirectory);

        if (context.PipelineTimer is null)
        {
            return await WriteReportsAsync(context, reportKind, symbol, format, outputDirectory, cancellationToken);
        }

        ReportOutcome outcome = new(null);
        await context.PipelineTimer.TimeWriteReportsAsync(async () =>
        {
            outcome = await WriteReportsAsync(context, reportKind, symbol, format, outputDirectory, cancellationToken);
        });

        return outcome;
    }

    private async Task<ReportOutcome> WriteReportsAsync(
        AnalysisContext context,
        string reportKind,
        string? symbol,
        ReportFormat format,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        ReportOutcome outcome;
        if (string.Equals(reportKind, "passport", StringComparison.OrdinalIgnoreCase))
        {
            var hashService = new DocumentHashService(_fileSystem);
            var cacheStore = new FileCacheStore(_fileSystem, hashService, context.CacheDir);
            var generator = new ArchitecturePassportGenerator(_fileSystem, cacheStore);
            await generator.WriteAsync(context, outputDirectory, cancellationToken);
            outcome = new ReportOutcome(null);
            await WriteStandardOutputsAsync(context, reportKind, outputDirectory, cancellationToken);
            return outcome;
        }

        if (string.Equals(reportKind, "project_graph", StringComparison.OrdinalIgnoreCase))
        {
            var graphData = context.PipelineTimer is null
                ? ProjectGraphReport.Create(context)
                : context.PipelineTimer.TimeBuildProjectGraph(() => ProjectGraphReport.Create(context));
            var projectGraphFileName = "project_graph";

            if (format is ReportFormat.Json or ReportFormat.Both)
            {
                var jsonPath = Path.Combine(outputDirectory, $"{projectGraphFileName}.json");
                var json = JsonSerializer.Serialize(graphData, new JsonSerializerOptions { WriteIndented = true });
                await _fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);
            }

            if (format is ReportFormat.Markdown or ReportFormat.Both)
            {
                var mdPath = Path.Combine(outputDirectory, $"{projectGraphFileName}.md");
                var markdown = ProjectGraphReport.BuildMarkdown(graphData);
                await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
            }

            outcome = new ReportOutcome(null);
            await WriteStandardOutputsAsync(context, reportKind, outputDirectory, cancellationToken);
            return outcome;
        }

        if (string.Equals(reportKind, "violations", StringComparison.OrdinalIgnoreCase))
        {
            var rulesData = await ArchitectureRulesReport.CreateAsync(context, cancellationToken);
            var violationsFileName = "violations";

            if (format is ReportFormat.Json or ReportFormat.Both)
            {
                var jsonPath = Path.Combine(outputDirectory, $"{violationsFileName}.json");
                var json = JsonSerializer.Serialize(rulesData, new JsonSerializerOptions { WriteIndented = true });
                await _fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);
            }

            if (format is ReportFormat.Markdown or ReportFormat.Both)
            {
                var mdPath = Path.Combine(outputDirectory, $"{violationsFileName}.md");
                var markdown = ArchitectureRulesReport.BuildMarkdown(rulesData);
                await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
            }

            outcome = new ReportOutcome(rulesData.Violations.Count);
            await WriteStandardOutputsAsync(context, reportKind, outputDirectory, cancellationToken);
            return outcome;
        }

        if (string.Equals(reportKind, "impact", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(symbol))
            {
                throw new ArgumentException("Impact analysis requires a symbol name.", nameof(symbol));
            }

            var impactData = await ImpactAnalysisReport.CreateAsync(context, symbol, cancellationToken);
            var impactFileName = "impact";

            if (format is ReportFormat.Json or ReportFormat.Both)
            {
                var jsonPath = Path.Combine(outputDirectory, $"{impactFileName}.json");
                var json = JsonSerializer.Serialize(impactData, new JsonSerializerOptions { WriteIndented = true });
                await _fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);
            }

            if (format is ReportFormat.Markdown or ReportFormat.Both)
            {
                var mdPath = Path.Combine(outputDirectory, $"{impactFileName}.md");
                var markdown = ImpactAnalysisReport.BuildMarkdown(impactData);
                await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
            }

            outcome = new ReportOutcome(null);
            await WriteStandardOutputsAsync(context, reportKind, outputDirectory, cancellationToken);
            return outcome;
        }

        var reportFileName = reportKind.ToLowerInvariant();
        if (string.Equals(reportKind, "scan", StringComparison.OrdinalIgnoreCase))
        {
            var scanData = ScanReceiptReport.Create(context);
            var scanPath = Path.Combine(outputDirectory, $"{reportFileName}.json");
            var scanJson = JsonSerializer.Serialize(scanData, new JsonSerializerOptions { WriteIndented = true });
            await _fileSystem.WriteAllTextAsync(scanPath, scanJson, cancellationToken);

            if (format is ReportFormat.Markdown or ReportFormat.Both)
            {
                var mdPath = Path.Combine(outputDirectory, $"{reportFileName}.md");
                var markdown = ScanReceiptReport.BuildMarkdown(scanData);
                await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
            }

            outcome = new ReportOutcome(null);
            await WriteStandardOutputsAsync(context, reportKind, outputDirectory, cancellationToken);
            return outcome;
        }

        var data = ReportData.Create(context, reportKind, symbol);

        if (format is ReportFormat.Json or ReportFormat.Both)
        {
            var jsonPath = Path.Combine(outputDirectory, $"{reportFileName}.json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await _fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);
        }

        if (format is ReportFormat.Markdown or ReportFormat.Both)
        {
            var mdPath = Path.Combine(outputDirectory, $"{reportFileName}.md");
            var markdown = BuildMarkdown(data);
            await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
        }

        outcome = new ReportOutcome(null);
        await WriteStandardOutputsAsync(context, reportKind, outputDirectory, cancellationToken);
        return outcome;
    }

    private async Task WriteStandardOutputsAsync(
        AnalysisContext context,
        string reportKind,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(reportKind, "scan", StringComparison.OrdinalIgnoreCase))
        {
            await ScanReceiptReport.WriteAsync(context, _fileSystem, outputDirectory, cancellationToken);
        }

        await ProjectsReport.WriteAsync(context, _fileSystem, outputDirectory, cancellationToken);
        await ScanSummaryReport.WriteAsync(context, _fileSystem, outputDirectory, cancellationToken);
        await SymbolIndexReport.WriteAsync(context, _fileSystem, outputDirectory, cancellationToken);
        await OutputReadmeReport.WriteAsync(context, _fileSystem, outputDirectory, cancellationToken);
    }

    private static string BuildMarkdown(ReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {data.Kind} report");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- Analysis version: {data.AnalysisVersion}");
        builder.AppendLine($"- MaxDegreeOfParallelism: {data.MaxDegreeOfParallelism}");
        if (!string.IsNullOrWhiteSpace(data.Symbol))
        {
            builder.AppendLine($"- Symbol: {data.Symbol}");
        }

        builder.AppendLine($"- Include globs: {FormatList(data.IncludeGlobs)}");
        builder.AppendLine($"- Exclude globs: {FormatList(data.ExcludeGlobs)}");
        return builder.ToString();
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", values);
    }

    private sealed record ReportData(
        string Kind,
        string SolutionPath,
        string AnalysisVersion,
        string? Symbol,
        int MaxDegreeOfParallelism,
        IReadOnlyList<string> IncludeGlobs,
        IReadOnlyList<string> ExcludeGlobs)
    {
        public static ReportData Create(AnalysisContext context, string kind, string? symbol)
        {
            var include = context.Config.IncludeGlobs.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            var exclude = context.Config.ExcludeGlobs.OrderBy(value => value, StringComparer.Ordinal).ToArray();

            return new ReportData(
                kind,
                context.SolutionPath,
                context.AnalysisVersion,
                symbol,
                context.MaxDegreeOfParallelism,
                include,
                exclude);
        }
    }
}
