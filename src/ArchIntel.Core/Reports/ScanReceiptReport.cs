using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public sealed record ScanReceiptProject(string ProjectId, string RoslynProjectId, string Name, string Path);

public sealed record ScanReceiptReportData(
    string Kind,
    string SolutionPath,
    string AnalysisVersion,
    string ToolVersion,
    string? CommitHash,
    string? CliInvocation,
    string OutputDir,
    string CacheDir,
    int MaxDegreeOfParallelism,
    bool FailOnLoadIssues,
    bool? StrictFailOnLoadIssues,
    bool? StrictFailOnViolations,
    IReadOnlyList<string> IncludeGlobs,
    IReadOnlyList<string> ExcludeGlobs,
    IReadOnlyList<ScanReceiptProject> Projects,
    IReadOnlyList<string> DeterministicRules);

public static class ScanReceiptReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ScanReceiptReportData Create(AnalysisContext context)
    {
        var include = context.Config.GetEffectiveIncludeGlobs().OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var exclude = context.Config.GetEffectiveExcludeGlobs().OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var projects = context.Solution.Projects
            .Select(project =>
            {
                var facts = ProjectFacts.Get(project, context.RepoRootPath, context.Config);
                return new ScanReceiptProject(facts.ProjectId, facts.RoslynProjectId, project.Name, CanonicalPath.Normalize(project.FilePath, context.RepoRootPath));
            })
            .OrderBy(project => project.Path, StringComparer.Ordinal)
            .ThenBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectId, StringComparer.Ordinal)
            .ToArray();

        return new ScanReceiptReportData(
            "scan",
            context.SolutionPath,
            context.AnalysisVersion,
            context.AnalysisVersion,
            TryGetCommitHash(context.RepoRootPath),
            context.CliInvocation,
            context.OutputDir,
            context.CacheDir,
            context.MaxDegreeOfParallelism,
            context.Config.FailOnLoadIssues,
            context.Config.Strict.FailOnLoadIssues,
            context.Config.Strict.FailOnViolations,
            include,
            exclude,
            projects,
            DeterministicRuleFormatter.SanitizeAndSort(InsightsReport.DeterministicRules));
    }

    public static string BuildMarkdown(ScanReceiptReportData data, SymbolIndexData? symbolData = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Scan configuration receipt");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- Analysis version: {data.AnalysisVersion}");
        builder.AppendLine($"- Tool version: {data.ToolVersion}");
        builder.AppendLine($"- Commit hash: {data.CommitHash ?? "(unknown)"}");
        builder.AppendLine($"- CLI invocation: {data.CliInvocation ?? "(unknown)"}");
        builder.AppendLine($"- Output directory: {data.OutputDir}");
        builder.AppendLine($"- Cache directory: {data.CacheDir}");
        builder.AppendLine($"- MaxDegreeOfParallelism: {data.MaxDegreeOfParallelism}");
        builder.AppendLine($"- Fail on load issues: {data.FailOnLoadIssues}");
        builder.AppendLine($"- Strict fail on load issues: {FormatOptionalBool(data.StrictFailOnLoadIssues)}");
        builder.AppendLine($"- Strict fail on violations: {FormatOptionalBool(data.StrictFailOnViolations)}");
        builder.AppendLine($"- Include globs: {FormatList(data.IncludeGlobs)}");
        builder.AppendLine($"- Exclude globs: {FormatList(data.ExcludeGlobs)}");
        builder.AppendLine($"- Deterministic insight rules: {FormatList(data.DeterministicRules)}");
        builder.AppendLine();
        builder.AppendLine("## Projects");
        builder.AppendLine();
        builder.AppendLine("| ProjectId | Name | Path |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var project in data.Projects)
        {
            builder.AppendLine($"| {project.ProjectId} | {project.Name} | {project.Path} |");
        }

        if (symbolData is not null)
        {
            AppendTopNamespacesByPublicSurface(builder, symbolData);
            AppendTopTypesPerNamespace(builder, symbolData);
        }

        return builder.ToString();
    }

    public static async Task WriteAsync(AnalysisContext context, IFileSystem fileSystem, string outputDirectory, CancellationToken cancellationToken)
    {
        var data = Create(context);
        var path = Path.Combine(outputDirectory, "scan.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string? TryGetCommitHash(string repoRootPath)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = repoRootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                return null;
            }

            process.WaitForExit(2000);
            if (process.ExitCode != 0)
            {
                return null;
            }

            var value = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    private static void AppendTopNamespacesByPublicSurface(StringBuilder builder, SymbolIndexData symbolData)
    {
        builder.AppendLine(); builder.AppendLine("## Top namespaces by public surface");
        var flattened = symbolData.Namespaces.SelectMany(project => project.Namespaces.Select(ns => new { project.ProjectName, Namespace = ns.Name, ns.PublicTypeCount, ns.TotalTypeCount, ns.DeclaredPublicMethodCount, ns.TotalMethodCount }))
            .OrderByDescending(entry => entry.DeclaredPublicMethodCount).ThenByDescending(entry => entry.PublicTypeCount).ThenByDescending(entry => entry.TotalMethodCount).ThenBy(entry => entry.ProjectName, StringComparer.Ordinal).ThenBy(entry => entry.Namespace, StringComparer.Ordinal).Take(10).ToArray();
        if (flattened.Length == 0) { builder.AppendLine("- (none)"); return; }
        foreach (var entry in flattened) builder.AppendLine($"- {entry.Namespace} ({entry.ProjectName}) — {entry.PublicTypeCount}/{entry.TotalTypeCount} public types, {entry.DeclaredPublicMethodCount}/{entry.TotalMethodCount} declared public methods");
    }

    private static void AppendTopTypesPerNamespace(StringBuilder builder, SymbolIndexData symbolData)
    {
        builder.AppendLine(); builder.AppendLine("## Top types per namespace");
        var namespaces = symbolData.Namespaces.SelectMany(project => project.Namespaces.Select(ns => new { project.ProjectName, Namespace = ns.Name, ns.TopTypes }))
            .Where(entry => entry.TopTypes.Count > 0).OrderBy(entry => entry.ProjectName, StringComparer.Ordinal).ThenBy(entry => entry.Namespace, StringComparer.Ordinal).ToArray();
        if (namespaces.Length == 0) { builder.AppendLine("- (none)"); return; }
        foreach (var entry in namespaces)
        {
            builder.AppendLine($"- {entry.Namespace} ({entry.ProjectName})");
            foreach (var type in entry.TopTypes) builder.AppendLine($"  - {type.Name} [{type.Visibility}] — {type.DeclaredPublicMethodCount}/{type.TotalMethodCount} declared public methods");
        }
    }

    private static string FormatList(IReadOnlyList<string> values) => values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static string FormatOptionalBool(bool? value) => value.HasValue ? value.Value.ToString() : "(default)";
}
