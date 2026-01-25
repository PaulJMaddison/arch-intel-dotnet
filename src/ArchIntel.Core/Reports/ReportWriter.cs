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

public sealed class ReportWriter
{
    private readonly IFileSystem _fileSystem;

    public ReportWriter() : this(new PhysicalFileSystem())
    {
    }

    public ReportWriter(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task WriteAsync(AnalysisContext context, string reportKind, string? symbol, ReportFormat format, CancellationToken cancellationToken)
    {
        var outputDirectory = context.OutputDir;
        _fileSystem.CreateDirectory(outputDirectory);

        if (context.PipelineTimer is null)
        {
            await WriteReportsAsync(context, reportKind, symbol, format, outputDirectory, cancellationToken);
        }
        else
        {
            await context.PipelineTimer.TimeWriteReportsAsync(
                () => WriteReportsAsync(context, reportKind, symbol, format, outputDirectory, cancellationToken));
        }
    }

    private async Task WriteReportsAsync(
        AnalysisContext context,
        string reportKind,
        string? symbol,
        ReportFormat format,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (string.Equals(reportKind, "project_graph", StringComparison.OrdinalIgnoreCase))
        {
            var graphData = context.PipelineTimer is null
                ? ProjectGraphReport.Create(context)
                : context.PipelineTimer.TimeBuildProjectGraph(() => ProjectGraphReport.Create(context));
            var baseFileName = "project_graph";

            if (format is ReportFormat.Json or ReportFormat.Both)
            {
                var jsonPath = Path.Combine(outputDirectory, $"{baseFileName}.json");
                var json = JsonSerializer.Serialize(graphData, new JsonSerializerOptions { WriteIndented = true });
                await _fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);
            }

            if (format is ReportFormat.Markdown or ReportFormat.Both)
            {
                var mdPath = Path.Combine(outputDirectory, $"{baseFileName}.md");
                var markdown = ProjectGraphReport.BuildMarkdown(graphData);
                await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
            }

            return;
        }

        var data = ReportData.Create(context, reportKind, symbol);
        var baseFileName = reportKind.ToLowerInvariant();

        if (format is ReportFormat.Json or ReportFormat.Both)
        {
            var jsonPath = Path.Combine(outputDirectory, $"{baseFileName}.json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await _fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);
        }

        if (format is ReportFormat.Markdown or ReportFormat.Both)
        {
            var mdPath = Path.Combine(outputDirectory, $"{baseFileName}.md");
            var markdown = BuildMarkdown(data);
            await _fileSystem.WriteAllTextAsync(mdPath, markdown, cancellationToken);
        }

        if (string.Equals(reportKind, "scan", StringComparison.OrdinalIgnoreCase))
        {
            await ScanSummaryReport.WriteAsync(context, _fileSystem, outputDirectory, cancellationToken);
        }
    }

    private static string BuildMarkdown(ReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# {data.Kind} report");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
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
                symbol,
                context.MaxDegreeOfParallelism,
                include,
                exclude);
        }
    }
}
