using System.Text;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public sealed class ArchitecturePassportGenerator
{
    private static readonly string[] LayerOrder =
    [
        "Presentation",
        "Application",
        "Domain",
        "Infrastructure",
        "Tests",
        "Unknown"
    ];

    private readonly IFileSystem _fileSystem;
    private readonly ICacheStore _cacheStore;

    public ArchitecturePassportGenerator(IFileSystem fileSystem, ICacheStore cacheStore)
    {
        _fileSystem = fileSystem;
        _cacheStore = cacheStore;
    }

    public async Task WriteAsync(AnalysisContext context, string outputDirectory, CancellationToken cancellationToken)
    {
        var markdown = await BuildAsync(context, cancellationToken);
        var path = Path.Combine(outputDirectory, "ARCHITECTURE_PASSPORT.md");
        await _fileSystem.WriteAllTextAsync(path, markdown, cancellationToken);
    }

    public async Task<string> BuildAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var graph = context.PipelineTimer is null
            ? ProjectGraphBuilder.Build(context.Solution, context.RepoRootPath)
            : context.PipelineTimer.TimeBuildProjectGraph(
                () => ProjectGraphBuilder.Build(context.Solution, context.RepoRootPath));

        var hashService = new DocumentHashService(_fileSystem);
        var cache = new DocumentCache(_cacheStore);
        var index = new SymbolIndex(new DocumentFilter(), hashService, cache, context.MaxDegreeOfParallelism);
        var symbolData = context.PipelineTimer is null
            ? await index.BuildAsync(context.Solution, context.AnalysisVersion, cancellationToken)
            : await context.PipelineTimer.TimeIndexSymbolsAsync(
                () => index.BuildAsync(context.Solution, context.AnalysisVersion, cancellationToken));

        return BuildMarkdown(context, graph, symbolData);
    }

    private static string BuildMarkdown(AnalysisContext context, ProjectGraph graph, SymbolIndexData symbolData)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Architecture Passport");
        builder.AppendLine();

        AppendOverview(builder, context, graph, symbolData);
        AppendLayers(builder, graph);
        AppendDependencyHotspots(builder, graph);
        AppendTopNamespaces(builder, symbolData);
        AppendTechDetection(builder, graph, symbolData);
        AppendDiHints(builder, symbolData);

        return builder.ToString();
    }

    private static void AppendOverview(
        StringBuilder builder,
        AnalysisContext context,
        ProjectGraph graph,
        SymbolIndexData symbolData)
    {
        var namespaceCount = symbolData.Namespaces.Sum(project => project.Namespaces.Count);

        builder.AppendLine("## Overview");
        builder.AppendLine($"- Solution: {context.SolutionPath}");
        builder.AppendLine($"- Analysis version: {context.AnalysisVersion}");
        builder.AppendLine($"- Projects: {graph.Nodes.Count}");
        builder.AppendLine($"- Dependencies: {graph.Edges.Count}");
        builder.AppendLine($"- Cycles: {graph.Cycles.Count}");
        builder.AppendLine($"- Namespaces indexed: {namespaceCount}");
        builder.AppendLine();
    }

    private static void AppendLayers(StringBuilder builder, ProjectGraph graph)
    {
        builder.AppendLine("## Layers and projects");

        var orderLookup = LayerOrder
            .Select((layer, index) => (layer, index))
            .ToDictionary(entry => entry.layer, entry => entry.index, StringComparer.Ordinal);

        var grouped = graph.Nodes
            .GroupBy(node => node.Layer)
            .Select(group => new
            {
                Layer = group.Key,
                Projects = group.OrderBy(node => node.Name, StringComparer.Ordinal).ToArray()
            })
            .OrderBy(group => orderLookup.TryGetValue(group.Layer, out var index) ? index : int.MaxValue)
            .ThenBy(group => group.Layer, StringComparer.Ordinal)
            .ToArray();

        foreach (var group in grouped)
        {
            builder.AppendLine($"- {group.Layer} ({group.Projects.Length})");
            foreach (var project in group.Projects)
            {
                var path = string.IsNullOrWhiteSpace(project.Path) ? "(no path)" : project.Path;
                builder.AppendLine($"  - {project.Name} — {path}");
            }
        }

        if (grouped.Length == 0)
        {
            builder.AppendLine("- (none)");
        }

        builder.AppendLine();
    }

    private static void AppendDependencyHotspots(StringBuilder builder, ProjectGraph graph)
    {
        builder.AppendLine("## Dependency hotspots");

        var nodesById = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var outbound = graph.Edges
            .GroupBy(edge => edge.FromId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var inbound = graph.Edges
            .GroupBy(edge => edge.ToId)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        var totals = graph.Nodes
            .Select(node => new
            {
                Node = node,
                Outgoing = outbound.GetValueOrDefault(node.Id, 0),
                Incoming = inbound.GetValueOrDefault(node.Id, 0)
            })
            .ToArray();

        builder.AppendLine("- Most outgoing dependencies:");
        foreach (var entry in totals
            .OrderByDescending(item => item.Outgoing)
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .Take(5))
        {
            builder.AppendLine($"  - {entry.Node.Name} ({entry.Node.Layer}) — {entry.Outgoing} outgoing, {entry.Incoming} incoming");
        }

        builder.AppendLine("- Most incoming dependencies:");
        foreach (var entry in totals
            .OrderByDescending(item => item.Incoming)
            .ThenBy(item => item.Node.Name, StringComparer.Ordinal)
            .Take(5))
        {
            builder.AppendLine($"  - {entry.Node.Name} ({entry.Node.Layer}) — {entry.Incoming} incoming, {entry.Outgoing} outgoing");
        }

        builder.AppendLine("- Cycles:");
        if (graph.Cycles.Count == 0)
        {
            builder.AppendLine("  - (none)");
        }
        else
        {
            foreach (var cycle in graph.Cycles)
            {
                var names = cycle
                    .Select(id => nodesById.TryGetValue(id, out var node) ? node.Name : id)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                builder.AppendLine($"  - {string.Join(" ↔ ", names)}");
            }
        }

        builder.AppendLine();
    }

    private static void AppendTopNamespaces(StringBuilder builder, SymbolIndexData symbolData)
    {
        builder.AppendLine("## Top namespaces");

        var flattened = symbolData.Namespaces
            .SelectMany(project => project.Namespaces.Select(ns => new
            {
                project.ProjectName,
                Namespace = ns.Name,
                ns.NamedTypeCount,
                ns.PublicMethodCount,
                Score = ns.NamedTypeCount + ns.PublicMethodCount
            }))
            .OrderByDescending(entry => entry.Score)
            .ThenByDescending(entry => entry.NamedTypeCount)
            .ThenByDescending(entry => entry.PublicMethodCount)
            .ThenBy(entry => entry.ProjectName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Namespace, StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        if (flattened.Length == 0)
        {
            builder.AppendLine("- (none)");
            builder.AppendLine();
            return;
        }

        foreach (var entry in flattened)
        {
            builder.AppendLine($"- {entry.Namespace} ({entry.ProjectName}) — {entry.NamedTypeCount} types, {entry.PublicMethodCount} public methods");
        }

        builder.AppendLine();
    }

    private static void AppendTechDetection(StringBuilder builder, ProjectGraph graph, SymbolIndexData symbolData)
    {
        builder.AppendLine("## Tech detection");

        var projectNames = graph.Nodes.Select(node => node.Name).ToArray();
        var namespaces = symbolData.Symbols
            .Select(entry => entry.Namespace)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var typeNames = symbolData.Symbols
            .Where(entry => string.Equals(entry.Kind, "NamedType", StringComparison.Ordinal))
            .Select(entry => entry.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var rules = TechRules;
        var detections = new List<string>();

        foreach (var rule in rules)
        {
            var evidence = new List<string>();

            foreach (var project in projectNames)
            {
                if (rule.ProjectHints.Any(hint => project.Contains(hint, StringComparison.OrdinalIgnoreCase)))
                {
                    evidence.Add($"project: {project}");
                }
            }

            foreach (var ns in namespaces)
            {
                if (rule.NamespacePrefixes.Any(prefix => ns.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    evidence.Add($"namespace: {ns}");
                }
            }

            foreach (var typeName in typeNames)
            {
                if (rule.TypeNames.Contains(typeName, StringComparer.Ordinal))
                {
                    evidence.Add($"type: {typeName}");
                    continue;
                }

                if (rule.TypeSuffixes.Any(suffix => typeName.EndsWith(suffix, StringComparison.Ordinal)))
                {
                    evidence.Add($"type: {typeName}");
                }
            }

            var uniqueEvidence = evidence
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .Take(3)
                .ToArray();

            if (uniqueEvidence.Length > 0)
            {
                detections.Add($"- {rule.Name} — signals: {string.Join(", ", uniqueEvidence)}");
            }
        }

        if (detections.Count == 0)
        {
            builder.AppendLine("- (none)");
        }
        else
        {
            foreach (var line in detections.OrderBy(item => item, StringComparer.Ordinal))
            {
                builder.AppendLine(line);
            }
        }

        builder.AppendLine();
    }

    private static void AppendDiHints(StringBuilder builder, SymbolIndexData symbolData)
    {
        builder.AppendLine("## DI wiring hints");

        var hints = new HashSet<string>(StringComparer.Ordinal);

        var methodHints = symbolData.Symbols
            .Where(entry => string.Equals(entry.Kind, "PublicMethod", StringComparison.Ordinal))
            .Where(entry => string.Equals(entry.Name, "ConfigureServices", StringComparison.Ordinal) ||
                            string.Equals(entry.Name, "Configure", StringComparison.Ordinal))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.ContainingType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Namespace, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in methodHints)
        {
            var container = entry.ContainingType ?? "(global)";
            var scope = string.IsNullOrWhiteSpace(entry.Namespace) ? entry.ProjectName : entry.Namespace!;
            hints.Add($"{container}.{entry.Name} ({scope})");
        }

        var typeHints = symbolData.Symbols
            .Where(entry => string.Equals(entry.Kind, "NamedType", StringComparison.Ordinal))
            .Where(entry => entry.Name.EndsWith("Module", StringComparison.Ordinal) ||
                            entry.Name.EndsWith("Installer", StringComparison.Ordinal) ||
                            entry.Name.Contains("DependencyInjection", StringComparison.Ordinal))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Namespace, StringComparer.Ordinal)
            .ToArray();

        foreach (var entry in typeHints)
        {
            var scope = string.IsNullOrWhiteSpace(entry.Namespace) ? entry.ProjectName : entry.Namespace!;
            hints.Add($"{entry.Name} ({scope})");
        }

        var namespaceHints = symbolData.Namespaces
            .SelectMany(project => project.Namespaces.Select(ns => ns.Name))
            .Where(name => name.Contains("DependencyInjection", StringComparison.Ordinal) ||
                           name.Contains("CompositionRoot", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        foreach (var ns in namespaceHints)
        {
            hints.Add($"Namespace {ns} (IServiceCollection extensions)");
        }

        if (hints.Count == 0)
        {
            builder.AppendLine("- (none)");
            builder.AppendLine();
            return;
        }

        foreach (var hint in hints.OrderBy(value => value, StringComparer.Ordinal))
        {
            builder.AppendLine($"- {hint}");
        }

        builder.AppendLine();
    }

    private static readonly IReadOnlyList<TechRule> TechRules =
    [
        new TechRule(
            "ASP.NET Core",
            ["Microsoft.AspNetCore"],
            ["Controller"],
            ["Startup", "Program"],
            ["api", "web", "frontend"]),
        new TechRule(
            "Entity Framework Core",
            ["Microsoft.EntityFrameworkCore"],
            ["DbContext"],
            ["DbContext"],
            ["data", "persistence", "infra"]),
        new TechRule(
            "MediatR",
            ["MediatR"],
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()),
        new TechRule(
            "Serilog",
            ["Serilog"],
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()),
        new TechRule(
            "FluentValidation",
            ["FluentValidation"],
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()),
        new TechRule(
            "Dapper",
            ["Dapper"],
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<string>()),
        new TechRule(
            "xUnit",
            ["Xunit"],
            Array.Empty<string>(),
            Array.Empty<string>(),
            ["test"]),
        new TechRule(
            "NUnit",
            ["NUnit"],
            Array.Empty<string>(),
            Array.Empty<string>(),
            ["test"])
    ];

    private sealed record TechRule(
        string Name,
        IReadOnlyList<string> NamespacePrefixes,
        IReadOnlyList<string> TypeSuffixes,
        IReadOnlyList<string> TypeNames,
        IReadOnlyList<string> ProjectHints);
}
