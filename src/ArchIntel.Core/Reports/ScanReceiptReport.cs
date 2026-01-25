using System.Text;
using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public sealed record ScanReceiptProject(string Name, string Path);

public sealed record ScanReceiptReportData(
    string Kind,
    string SolutionPath,
    string AnalysisVersion,
    string OutputDir,
    string CacheDir,
    int MaxDegreeOfParallelism,
    bool FailOnLoadIssues,
    bool? StrictFailOnLoadIssues,
    bool? StrictFailOnViolations,
    IReadOnlyList<string> IncludeGlobs,
    IReadOnlyList<string> ExcludeGlobs,
    IReadOnlyList<ScanReceiptProject> Projects);

public static class ScanReceiptReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static ScanReceiptReportData Create(AnalysisContext context)
    {
        var include = context.Config.IncludeGlobs.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var exclude = context.Config.ExcludeGlobs.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var projects = context.Solution.Projects
            .Select(project => new ScanReceiptProject(project.Name, GetDisplayPath(project.FilePath, context.RepoRootPath)))
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .ToArray();

        return new ScanReceiptReportData(
            "scan",
            context.SolutionPath,
            context.AnalysisVersion,
            context.OutputDir,
            context.CacheDir,
            context.MaxDegreeOfParallelism,
            context.Config.FailOnLoadIssues,
            context.Config.Strict.FailOnLoadIssues,
            context.Config.Strict.FailOnViolations,
            include,
            exclude,
            projects);
    }

    public static string BuildMarkdown(ScanReceiptReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Scan configuration receipt");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- Analysis version: {data.AnalysisVersion}");
        builder.AppendLine($"- Output directory: {data.OutputDir}");
        builder.AppendLine($"- Cache directory: {data.CacheDir}");
        builder.AppendLine($"- MaxDegreeOfParallelism: {data.MaxDegreeOfParallelism}");
        builder.AppendLine($"- Fail on load issues: {data.FailOnLoadIssues}");
        builder.AppendLine($"- Strict fail on load issues: {FormatOptionalBool(data.StrictFailOnLoadIssues)}");
        builder.AppendLine($"- Strict fail on violations: {FormatOptionalBool(data.StrictFailOnViolations)}");
        builder.AppendLine($"- Include globs: {FormatList(data.IncludeGlobs)}");
        builder.AppendLine($"- Exclude globs: {FormatList(data.ExcludeGlobs)}");
        builder.AppendLine();
        builder.AppendLine("## Projects");
        if (data.Projects.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("*(none)*");
            return builder.ToString();
        }

        builder.AppendLine();
        builder.AppendLine("| Name | Path |");
        builder.AppendLine("| --- | --- |");
        foreach (var project in data.Projects)
        {
            builder.AppendLine($"| {project.Name} | {project.Path} |");
        }

        return builder.ToString();
    }

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var data = Create(context);
        var path = Path.Combine(outputDirectory, "scan.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string FormatList(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "(none)";
        }

        return string.Join(", ", values);
    }

    private static string GetDisplayPath(string? filePath, string repoRootPath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        var fullPath = Path.GetFullPath(filePath);
        var root = Path.GetFullPath(repoRootPath);
        if (!string.IsNullOrWhiteSpace(root) && fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(root, fullPath);
        }

        return fullPath;
    }

    private static string FormatOptionalBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString() : "(default)";
    }
}
