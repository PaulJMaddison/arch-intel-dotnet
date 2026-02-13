using System.Collections.ObjectModel;
using ArchIntel.Configuration;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public static class ProjectGraphBuilder
{
    public static ProjectGraph Build(Solution solution, string repoRootPath, AnalysisConfig? config = null)
    {
        config ??= new AnalysisConfig();
        var projects = solution.Projects.ToArray();
        var idMap = new Dictionary<ProjectId, string>();
        var nodeMap = new Dictionary<ProjectId, ProjectNode>();

        foreach (var project in projects)
        {
            var facts = ProjectFacts.Get(project, repoRootPath, config);
            var path = CanonicalPath.Normalize(project.FilePath, repoRootPath);
            var node = new ProjectNode(
                facts.ProjectId,
                project.Name,
                path,
                facts.Layer,
                facts.LayerReason.ToString(),
                facts.LayerRuleMatched,
                facts.RoslynProjectId,
                facts.IsTestProject,
                facts.TestDetectionReason.ToString());

            idMap[project.Id] = facts.ProjectId;
            nodeMap[project.Id] = node;
        }

        var nodes = nodeMap.Values
            .OrderBy(node => node.Path, StringComparer.Ordinal)
            .ThenBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToList();

        var edgeSet = new HashSet<(string FromId, string ToId)>();
        foreach (var project in projects)
        {
            var fromId = idMap[project.Id];
            foreach (var reference in project.ProjectReferences)
            {
                if (!idMap.TryGetValue(reference.ProjectId, out var toId))
                {
                    continue;
                }

                edgeSet.Add((fromId, toId));
            }
        }

        var edges = edgeSet
            .OrderBy(edge => edge.FromId, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToId, StringComparer.Ordinal)
            .Select(edge => new ProjectEdge(edge.FromId, edge.ToId))
            .ToList();

        var adjacency = BuildAdjacency(nodes, edges);
        var cycles = FindCycles(adjacency);

        return ProjectGraph.Create(
            new ReadOnlyCollection<ProjectNode>(nodes),
            new ReadOnlyCollection<ProjectEdge>(edges),
            new ReadOnlyCollection<IReadOnlyList<string>>(cycles.ToList()));
    }
    private static Dictionary<string, List<string>> BuildAdjacency(
        IReadOnlyList<ProjectNode> nodes,
        IReadOnlyList<ProjectEdge> edges)
    {
        var adjacency = nodes.ToDictionary(node => node.Id, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            if (adjacency.TryGetValue(edge.FromId, out var list))
            {
                list.Add(edge.ToId);
            }
        }

        foreach (var list in adjacency.Values)
        {
            list.Sort(StringComparer.Ordinal);
        }

        return adjacency;
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(Dictionary<string, List<string>> adjacency)
    {
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var components = new List<List<string>>();

        void StrongConnect(string nodeId)
        {
            indices[nodeId] = index;
            lowLinks[nodeId] = index;
            index += 1;
            stack.Push(nodeId);
            onStack.Add(nodeId);

            foreach (var neighbor in adjacency[nodeId])
            {
                if (!indices.ContainsKey(neighbor))
                {
                    StrongConnect(neighbor);
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], lowLinks[neighbor]);
                }
                else if (onStack.Contains(neighbor))
                {
                    lowLinks[nodeId] = Math.Min(lowLinks[nodeId], indices[neighbor]);
                }
            }

            if (lowLinks[nodeId] != indices[nodeId])
            {
                return;
            }

            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            } while (!string.Equals(current, nodeId, StringComparison.Ordinal));

            components.Add(component);
        }

        foreach (var nodeId in adjacency.Keys.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!indices.ContainsKey(nodeId))
            {
                StrongConnect(nodeId);
            }
        }

        var cycles = new List<IReadOnlyList<string>>();
        foreach (var component in components)
        {
            if (component.Count == 1)
            {
                var only = component[0];
                if (!adjacency[only].Contains(only, StringComparer.Ordinal))
                {
                    continue;
                }
            }

            component.Sort(StringComparer.Ordinal);
            cycles.Add(component.AsReadOnly());
        }

        return cycles
            .OrderBy(cycle => cycle[0], StringComparer.Ordinal)
            .ThenBy(cycle => cycle.Count)
            .ToArray();
    }
}
