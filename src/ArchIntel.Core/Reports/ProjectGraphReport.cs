using System.Text;
using ArchIntel.Analysis;

namespace ArchIntel.Reports;

public sealed record ProjectGraphReportData(
    string Kind,
    string SolutionPath,
    string AnalysisVersion,
    IReadOnlyList<ProjectNode> Nodes,
    IReadOnlyList<ProjectEdge> Edges,
    IReadOnlyList<IReadOnlyList<string>> Cycles)
{
    public static ProjectGraphReportData Create(AnalysisContext context)
    {
        var graph = ProjectGraphBuilder.Build(context.Solution, context.RepoRootPath);
        return new ProjectGraphReportData(
            "project_graph",
            context.SolutionPath,
            context.AnalysisVersion,
            graph.Nodes,
            graph.Edges,
            graph.Cycles);
    }
}

public static class ProjectGraphReport
{
    public static ProjectGraphReportData Create(AnalysisContext context)
    {
        return ProjectGraphReportData.Create(context);
    }

    public static string BuildMarkdown(ProjectGraphReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Project graph");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- AnalysisVersion: {data.AnalysisVersion}");
        builder.AppendLine($"- Nodes: {data.Nodes.Count}");
        builder.AppendLine($"- Edges: {data.Edges.Count}");
        builder.AppendLine($"- Cycles: {data.Cycles.Count}");
        builder.AppendLine();
        builder.AppendLine("## Nodes");
        builder.AppendLine();
        builder.AppendLine("| Id | Name | Path | Layer |");
        builder.AppendLine("| --- | --- | --- | --- |");
        foreach (var node in data.Nodes)
        {
            builder.AppendLine($"| {node.Id} | {node.Name} | {node.Path} | {node.Layer} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Edges");
        builder.AppendLine();
        if (data.Edges.Count == 0)
        {
            builder.AppendLine("*(none)*");
        }
        else
        {
            builder.AppendLine("| From | To |");
            builder.AppendLine("| --- | --- |");
            foreach (var edge in data.Edges)
            {
                builder.AppendLine($"| {edge.FromId} | {edge.ToId} |");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Cycles");
        builder.AppendLine();
        if (data.Cycles.Count == 0)
        {
            builder.AppendLine("*(none)*");
        }
        else
        {
            var index = 1;
            foreach (var cycle in data.Cycles)
            {
                builder.AppendLine($"{index}. {string.Join(" -> ", cycle)}");
                index += 1;
            }
        }

        return builder.ToString();
    }
}
