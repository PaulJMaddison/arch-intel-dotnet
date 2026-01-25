using System.Text.Json;
using ArchIntel.Configuration;
using ArchIntel.IO;

namespace ArchIntel.Analysis;

public sealed record ArchitectureRuleViolation(
    string FromProject,
    string FromLayer,
    string ToProject,
    string ToLayer,
    IReadOnlyList<string> AllowedLayers);

public sealed record ProjectNodeSnapshot(
    string Id,
    string Name,
    string Layer,
    string Path);

public sealed record ProjectEdgeSnapshot(
    string FromId,
    string FromName,
    string FromLayer,
    string ToId,
    string ToName,
    string ToLayer);

public sealed record ArchitectureGraphSnapshot(
    IReadOnlyList<ProjectNodeSnapshot> Nodes,
    IReadOnlyList<ProjectEdgeSnapshot> Edges);

public sealed record ArchitectureDriftReport(
    bool BaselineAvailable,
    IReadOnlyList<ProjectNodeSnapshot> AddedProjects,
    IReadOnlyList<ProjectNodeSnapshot> RemovedProjects,
    IReadOnlyList<ProjectEdgeSnapshot> AddedDependencies,
    IReadOnlyList<ProjectEdgeSnapshot> RemovedDependencies);

public sealed record ArchitectureRulesResult(
    string SolutionPath,
    string AnalysisVersion,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<LayerRule> Rules,
    IReadOnlyList<ArchitectureRuleViolation> Violations,
    ArchitectureDriftReport Drift);

public sealed class ArchitectureRulesEngine
{
    private const string CacheFileName = "architecture_graph.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fileSystem;

    public ArchitectureRulesEngine(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public async Task<ArchitectureRulesResult> AnalyzeAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var graph = context.PipelineTimer is null
            ? ProjectGraphBuilder.Build(context.Solution, context.RepoRootPath)
            : context.PipelineTimer.TimeBuildProjectGraph(
                () => ProjectGraphBuilder.Build(context.Solution, context.RepoRootPath));

        var rules = BuildEffectiveRules(context.Config.ArchitectureRules);
        var violations = DetectViolations(graph, rules);

        var snapshot = CreateSnapshot(graph);
        var drift = await DetectDriftAsync(context.CacheDir, snapshot, cancellationToken);

        return new ArchitectureRulesResult(
            context.SolutionPath,
            context.AnalysisVersion,
            DateTimeOffset.UtcNow,
            rules,
            violations,
            drift);
    }

    private static IReadOnlyList<LayerRule> BuildEffectiveRules(ArchitectureRulesConfig config)
    {
        var rules = new Dictionary<string, LayerRule>(StringComparer.OrdinalIgnoreCase);
        if (config.UseDefaultLayerRules)
        {
            foreach (var rule in GetDefaultRules())
            {
                rules[rule.FromLayer] = rule;
            }
        }

        foreach (var rule in config.LayerRules)
        {
            rules[rule.FromLayer] = rule;
        }

        return rules.Values
            .OrderBy(rule => rule.FromLayer, StringComparer.Ordinal)
            .Select(rule => new LayerRule(rule.FromLayer, rule.AllowedLayers.OrderBy(layer => layer, StringComparer.Ordinal).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<LayerRule> GetDefaultRules()
    {
        return new[]
        {
            new LayerRule("Presentation", new[] { "Presentation", "Application" }),
            new LayerRule("Application", new[] { "Application", "Domain" }),
            new LayerRule("Domain", new[] { "Domain" }),
            new LayerRule("Infrastructure", new[] { "Infrastructure", "Application", "Domain" }),
            new LayerRule("Tests", new[] { "Tests", "Presentation", "Application", "Domain", "Infrastructure", "Unknown" }),
            new LayerRule("Unknown", new[] { "Presentation", "Application", "Domain", "Infrastructure", "Tests", "Unknown" })
        };
    }

    private static IReadOnlyList<ArchitectureRuleViolation> DetectViolations(
        ProjectGraph graph,
        IReadOnlyList<LayerRule> rules)
    {
        var rulesByLayer = rules.ToDictionary(rule => rule.FromLayer, StringComparer.OrdinalIgnoreCase);
        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var violations = new List<ArchitectureRuleViolation>();

        foreach (var edge in graph.Edges)
        {
            if (!nodesById.TryGetValue(edge.FromId, out var fromNode) ||
                !nodesById.TryGetValue(edge.ToId, out var toNode))
            {
                continue;
            }

            if (!rulesByLayer.TryGetValue(fromNode.Layer, out var rule))
            {
                continue;
            }

            var allowed = rule.AllowedLayers;
            if (allowed.Contains(toNode.Layer, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            violations.Add(new ArchitectureRuleViolation(
                fromNode.Name,
                fromNode.Layer,
                toNode.Name,
                toNode.Layer,
                allowed.OrderBy(layer => layer, StringComparer.Ordinal).ToArray()));
        }

        return violations
            .OrderBy(v => v.FromLayer, StringComparer.Ordinal)
            .ThenBy(v => v.FromProject, StringComparer.Ordinal)
            .ThenBy(v => v.ToProject, StringComparer.Ordinal)
            .ToArray();
    }

    private static ArchitectureGraphSnapshot CreateSnapshot(ProjectGraph graph)
    {
        var nodes = graph.Nodes
            .OrderBy(node => node.Id, StringComparer.Ordinal)
            .Select(node => new ProjectNodeSnapshot(node.Id, node.Name, node.Layer, node.Path))
            .ToArray();

        var nodeLookup = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        var edges = graph.Edges
            .OrderBy(edge => edge.FromId, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToId, StringComparer.Ordinal)
            .Select(edge =>
            {
                var fromNode = nodeLookup[edge.FromId];
                var toNode = nodeLookup[edge.ToId];
                return new ProjectEdgeSnapshot(
                    fromNode.Id,
                    fromNode.Name,
                    fromNode.Layer,
                    toNode.Id,
                    toNode.Name,
                    toNode.Layer);
            })
            .ToArray();

        return new ArchitectureGraphSnapshot(nodes, edges);
    }

    private async Task<ArchitectureDriftReport> DetectDriftAsync(
        string cacheDir,
        ArchitectureGraphSnapshot current,
        CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(cacheDir, CacheFileName);
        var previous = await TryLoadSnapshotAsync(cachePath, cancellationToken);

        var drift = ComputeDrift(previous, current);

        _fileSystem.CreateDirectory(cacheDir);
        var json = JsonSerializer.Serialize(current, JsonOptions);
        await _fileSystem.WriteAllTextAsync(cachePath, json, cancellationToken);

        return drift;
    }

    private async Task<ArchitectureGraphSnapshot?> TryLoadSnapshotAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<ArchitectureGraphSnapshot>(json, JsonOptions);
    }

    private static ArchitectureDriftReport ComputeDrift(
        ArchitectureGraphSnapshot? previous,
        ArchitectureGraphSnapshot current)
    {
        if (previous is null)
        {
            return new ArchitectureDriftReport(
                false,
                Array.Empty<ProjectNodeSnapshot>(),
                Array.Empty<ProjectNodeSnapshot>(),
                Array.Empty<ProjectEdgeSnapshot>(),
                Array.Empty<ProjectEdgeSnapshot>());
        }

        var previousNodes = previous.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var currentNodes = current.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        var addedProjects = current.Nodes
            .Where(node => !previousNodes.ContainsKey(node.Id))
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var removedProjects = previous.Nodes
            .Where(node => !currentNodes.ContainsKey(node.Id))
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        var previousEdges = previous.Edges.ToDictionary(edge => (edge.FromId, edge.ToId), edge => edge, new EdgeKeyComparer());
        var currentEdges = current.Edges.ToDictionary(edge => (edge.FromId, edge.ToId), edge => edge, new EdgeKeyComparer());

        var addedDependencies = current.Edges
            .Where(edge => !previousEdges.ContainsKey((edge.FromId, edge.ToId)))
            .OrderBy(edge => edge.FromName, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToName, StringComparer.Ordinal)
            .ToArray();

        var removedDependencies = previous.Edges
            .Where(edge => !currentEdges.ContainsKey((edge.FromId, edge.ToId)))
            .OrderBy(edge => edge.FromName, StringComparer.Ordinal)
            .ThenBy(edge => edge.ToName, StringComparer.Ordinal)
            .ToArray();

        return new ArchitectureDriftReport(
            true,
            addedProjects,
            removedProjects,
            addedDependencies,
            removedDependencies);
    }

    private sealed class EdgeKeyComparer : IEqualityComparer<(string FromId, string ToId)>
    {
        public bool Equals((string FromId, string ToId) x, (string FromId, string ToId) y)
        {
            return StringComparer.Ordinal.Equals(x.FromId, y.FromId)
                && StringComparer.Ordinal.Equals(x.ToId, y.ToId);
        }

        public int GetHashCode((string FromId, string ToId) obj)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.FromId),
                StringComparer.Ordinal.GetHashCode(obj.ToId));
        }
    }
}
