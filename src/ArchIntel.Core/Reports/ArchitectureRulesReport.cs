using System.Text;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public static class ArchitectureRulesReport
{
    public static async Task<ArchitectureRulesResult> CreateAsync(
        AnalysisContext context,
        CancellationToken cancellationToken)
    {
        var engine = new ArchitectureRulesEngine(new PhysicalFileSystem());
        return await engine.AnalyzeAsync(context, cancellationToken);
    }

    public static string BuildMarkdown(ArchitectureRulesResult data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Architecture rules report");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- Analysis version: {data.AnalysisVersion}");
        builder.AppendLine($"- Violations: {data.Violations.Count}");
        builder.AppendLine();

        builder.AppendLine("## Effective rules");
        builder.AppendLine();
        if (data.Rules.Count == 0)
        {
            builder.AppendLine("(no rules configured)");
        }
        else
        {
            foreach (var rule in data.Rules)
            {
                var allowed = rule.AllowedLayers.Count == 0
                    ? "(none)"
                    : string.Join(", ", rule.AllowedLayers);
                builder.AppendLine($"- {rule.FromLayer} -> {allowed}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Violations");
        builder.AppendLine();
        if (data.Violations.Count == 0)
        {
            builder.AppendLine("No violations detected.");
        }
        else
        {
            foreach (var violation in data.Violations)
            {
                var allowed = string.Join(", ", violation.AllowedLayers);
                builder.AppendLine($"- {violation.FromProject} ({violation.FromLayer}) -> {violation.ToProject} ({violation.ToLayer})");
                builder.AppendLine($"  - Allowed layers: {allowed}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Drift");
        builder.AppendLine();
        if (!data.Drift.BaselineAvailable)
        {
            builder.AppendLine("No cached baseline found. Drift will be reported after the next run.");
            return builder.ToString();
        }

        builder.AppendLine($"- Added projects: {data.Drift.AddedProjects.Count}");
        builder.AppendLine($"- Removed projects: {data.Drift.RemovedProjects.Count}");
        builder.AppendLine($"- Added dependencies: {data.Drift.AddedDependencies.Count}");
        builder.AppendLine($"- Removed dependencies: {data.Drift.RemovedDependencies.Count}");
        builder.AppendLine();

        AppendProjects(builder, "Added projects", data.Drift.AddedProjects);
        AppendProjects(builder, "Removed projects", data.Drift.RemovedProjects);
        AppendDependencies(builder, "Added dependencies", data.Drift.AddedDependencies);
        AppendDependencies(builder, "Removed dependencies", data.Drift.RemovedDependencies);

        return builder.ToString();
    }

    private static void AppendProjects(
        StringBuilder builder,
        string title,
        IReadOnlyList<ProjectNodeSnapshot> projects)
    {
        builder.AppendLine($"### {title}");
        builder.AppendLine();
        if (projects.Count == 0)
        {
            builder.AppendLine("(none)");
            builder.AppendLine();
            return;
        }

        foreach (var project in projects)
        {
            builder.AppendLine($"- {project.Name} ({project.Layer}) — {project.Path}");
        }

        builder.AppendLine();
    }

    private static void AppendDependencies(
        StringBuilder builder,
        string title,
        IReadOnlyList<ProjectEdgeSnapshot> dependencies)
    {
        builder.AppendLine($"### {title}");
        builder.AppendLine();
        if (dependencies.Count == 0)
        {
            builder.AppendLine("(none)");
            builder.AppendLine();
            return;
        }

        foreach (var edge in dependencies)
        {
            builder.AppendLine($"- {edge.FromName} ({edge.FromLayer}) -> {edge.ToName} ({edge.ToLayer})");
        }

        builder.AppendLine();
    }
}
