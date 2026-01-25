using System.Text;
using System.Text.Json;
using ArchIntel.Analysis;

namespace ArchIntel.Reports;

public enum ReportFormat
{
    Json,
    Markdown,
    Both
}

public sealed class ReportWriter
{
    public async Task WriteAsync(AnalysisContext context, string reportKind, string? symbol, ReportFormat format, CancellationToken cancellationToken)
    {
        var outputDirectory = context.OutputDir;
        Directory.CreateDirectory(outputDirectory);

        var data = ReportData.Create(context, reportKind, symbol);
        var baseFileName = reportKind.ToLowerInvariant();

        if (format is ReportFormat.Json or ReportFormat.Both)
        {
            var jsonPath = Path.Combine(outputDirectory, $"{baseFileName}.json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonPath, json, cancellationToken);
        }

        if (format is ReportFormat.Markdown or ReportFormat.Both)
        {
            var mdPath = Path.Combine(outputDirectory, $"{baseFileName}.md");
            var markdown = BuildMarkdown(data);
            await File.WriteAllTextAsync(mdPath, markdown, cancellationToken);
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
