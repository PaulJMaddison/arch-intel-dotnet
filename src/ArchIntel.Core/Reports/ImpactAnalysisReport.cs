using System.Text;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public sealed record ImpactAnalysisReportData(
    string SolutionPath,
    string AnalysisVersion,
    string Symbol,
    bool Found,
    ImpactDefinitionLocation? DefinitionLocation,
    IReadOnlyList<string> ImpactedProjects,
    IReadOnlyList<string> ImpactedFiles,
    int TotalReferences,
    IReadOnlyList<string> Suggestions);

public static class ImpactAnalysisReport
{
    public static async Task<ImpactAnalysisReportData> CreateAsync(
        AnalysisContext context,
        string symbol,
        CancellationToken cancellationToken)
    {
        var fileSystem = new PhysicalFileSystem();
        var hashService = new DocumentHashService(fileSystem);
        var cacheStore = new FileCacheStore(fileSystem, hashService, context.CacheDir);
        var cache = new DocumentCache(cacheStore);
        var engine = new ImpactAnalysisEngine(new DocumentFilter(), hashService, cache, context.MaxDegreeOfParallelism);

        var result = await engine.AnalyzeAsync(context.Solution, context.AnalysisVersion, symbol, cancellationToken);

        return new ImpactAnalysisReportData(
            context.SolutionPath,
            context.AnalysisVersion,
            result.Symbol,
            result.Found,
            result.DefinitionLocation,
            result.ImpactedProjects,
            result.ImpactedFiles,
            result.TotalReferences,
            result.Suggestions);
    }

    public static string BuildMarkdown(ImpactAnalysisReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Impact analysis report");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- Analysis version: {data.AnalysisVersion}");
        builder.AppendLine($"- Symbol: {data.Symbol}");
        builder.AppendLine($"- Found: {(data.Found ? "Yes" : "No")}");
        builder.AppendLine($"- Total references: {data.TotalReferences}");
        builder.AppendLine();

        if (data.DefinitionLocation is not null)
        {
            builder.AppendLine("## Definition");
            builder.AppendLine();
            builder.AppendLine($"- File: {data.DefinitionLocation.FilePath}");
            builder.AppendLine($"- Line: {data.DefinitionLocation.Line}");
            builder.AppendLine($"- Column: {data.DefinitionLocation.Column}");
            builder.AppendLine();
        }

        builder.AppendLine("## Impacted projects");
        builder.AppendLine();
        builder.AppendLine(FormatList(data.ImpactedProjects));
        builder.AppendLine();

        builder.AppendLine("## Impacted files");
        builder.AppendLine();
        builder.AppendLine(FormatList(data.ImpactedFiles));
        builder.AppendLine();

        if (!data.Found)
        {
            builder.AppendLine("## Suggestions");
            builder.AppendLine();
            builder.AppendLine(FormatList(data.Suggestions));
        }

        return builder.ToString();
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "(none)";
        }

        return string.Join(Environment.NewLine, values.Select(value => $"- {value}"));
    }
}
