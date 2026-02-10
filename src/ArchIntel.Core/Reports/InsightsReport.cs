using System.Text;
using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public sealed record InsightsProjectMetric(string ProjectId, string ProjectName, int FanIn, int FanOut, int Score);

public sealed record InsightsCycleMetric(
    IReadOnlyList<string> ProjectIds,
    int Length,
    bool ContainsTestProject,
    int TotalFanOut,
    int SeverityScore);

public sealed record InsightsNamespaceMetric(string Namespace, int PublicTypeCount, int PublicMethodCount, int TotalMethodCount, int Score);

public sealed record InsightsPackageDriftHotspot(
    string PackageId,
    IReadOnlyList<string> Versions,
    int DistinctMajorCount,
    bool HasPrerelease,
    int ProjectCount,
    int Score);

public sealed record InsightsReportData(
    string Kind,
    string SolutionPath,
    string AnalysisVersion,
    IReadOnlyList<InsightsProjectMetric> TopFanInProjects,
    IReadOnlyList<InsightsProjectMetric> TopFanOutProjects,
    IReadOnlyList<InsightsProjectMetric> CoreProjects,
    IReadOnlyList<InsightsProjectMetric> RiskyProjects,
    IReadOnlyList<InsightsCycleMetric> CycleSeverity,
    IReadOnlyList<InsightsNamespaceMetric> BiggestNamespacesByPublicSurface,
    IReadOnlyList<InsightsPackageDriftHotspot> PackageDriftHotspots,
    IReadOnlyList<string> DeterministicRules);

public static class InsightsReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] TestPackageMarkers = ["xunit", "nunit", "mstest"];

    private static readonly string[] Rules =
    [
        "top_fan_in_order=fan_in_desc,fan_out_asc,project_name_asc,project_id_asc",
        "top_fan_out_order=fan_out_desc,fan_in_desc,project_name_asc,project_id_asc",
        "core_score=fan_in*10-fan_out*3;include=fan_in>0;order=score_desc,fan_in_desc,fan_out_asc,project_name_asc,project_id_asc",
        "risky_score=fan_out*20+cycle_count*30+fan_in;include=fan_out>0&&cycle_count>0;order=score_desc,cycle_count_desc,fan_out_desc,project_name_asc,project_id_asc",
        "cycle_severity_score=length*100+(contains_test?35:0)+total_fan_out*5;order=severity_desc,length_desc,first_project_id_asc",
        "namespace_surface_score=public_method_count*4+public_type_count*2+total_method_count;order=score_desc,namespace_asc",
        "package_drift_score=distinct_major_count*50+(has_prerelease?25:0)+project_count*2;include=distinct_major_count>1||has_prerelease;order=score_desc,package_id_asc"
    ];

    public static IReadOnlyList<string> DeterministicRules => Rules;

    public static InsightsReportData Create(AnalysisContext context, SymbolIndexData symbolData)
    {
        var graph = ProjectGraphBuilder.Build(context.Solution, context.RepoRootPath);
        var packages = PackagesReport.Create(context);

        var nameById = graph.Nodes.ToDictionary(node => node.Id, node => node.Name, StringComparer.Ordinal);
        var fanInByProject = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        var fanOutByProject = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);

        foreach (var edge in graph.Edges)
        {
            fanOutByProject[edge.FromId] += 1;
            fanInByProject[edge.ToId] += 1;
        }

        var cycleCountByProject = graph.Nodes.ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
        foreach (var cycle in graph.Cycles)
        {
            foreach (var projectId in cycle)
            {
                cycleCountByProject[projectId] += 1;
            }
        }

        var testProjectIds = ResolveTestProjects(packages, graph);
        var projectMetrics = graph.Nodes
            .Select(node => new
            {
                node.Id,
                node.Name,
                FanIn = fanInByProject[node.Id],
                FanOut = fanOutByProject[node.Id],
                CycleCount = cycleCountByProject[node.Id]
            })
            .ToArray();

        var topFanIn = projectMetrics
            .OrderByDescending(metric => metric.FanIn)
            .ThenBy(metric => metric.FanOut)
            .ThenBy(metric => metric.Name, StringComparer.Ordinal)
            .ThenBy(metric => metric.Id, StringComparer.Ordinal)
            .Take(10)
            .Select(metric => new InsightsProjectMetric(metric.Id, metric.Name, metric.FanIn, metric.FanOut, metric.FanIn))
            .ToArray();

        var topFanOut = projectMetrics
            .OrderByDescending(metric => metric.FanOut)
            .ThenByDescending(metric => metric.FanIn)
            .ThenBy(metric => metric.Name, StringComparer.Ordinal)
            .ThenBy(metric => metric.Id, StringComparer.Ordinal)
            .Take(10)
            .Select(metric => new InsightsProjectMetric(metric.Id, metric.Name, metric.FanIn, metric.FanOut, metric.FanOut))
            .ToArray();

        var coreProjects = projectMetrics
            .Select(metric => new
            {
                metric.Id,
                metric.Name,
                metric.FanIn,
                metric.FanOut,
                Score = (metric.FanIn * 10) - (metric.FanOut * 3)
            })
            .Where(metric => metric.FanIn > 0)
            .OrderByDescending(metric => metric.Score)
            .ThenByDescending(metric => metric.FanIn)
            .ThenBy(metric => metric.FanOut)
            .ThenBy(metric => metric.Name, StringComparer.Ordinal)
            .ThenBy(metric => metric.Id, StringComparer.Ordinal)
            .Take(10)
            .Select(metric => new InsightsProjectMetric(metric.Id, metric.Name, metric.FanIn, metric.FanOut, metric.Score))
            .ToArray();

        var riskyProjects = projectMetrics
            .Select(metric => new
            {
                metric.Id,
                metric.Name,
                metric.FanIn,
                metric.FanOut,
                metric.CycleCount,
                Score = (metric.FanOut * 20) + (metric.CycleCount * 30) + metric.FanIn
            })
            .Where(metric => metric.FanOut > 0 && metric.CycleCount > 0)
            .OrderByDescending(metric => metric.Score)
            .ThenByDescending(metric => metric.CycleCount)
            .ThenByDescending(metric => metric.FanOut)
            .ThenBy(metric => metric.Name, StringComparer.Ordinal)
            .ThenBy(metric => metric.Id, StringComparer.Ordinal)
            .Take(10)
            .Select(metric => new InsightsProjectMetric(metric.Id, metric.Name, metric.FanIn, metric.FanOut, metric.Score))
            .ToArray();

        var cycleSeverity = graph.Cycles
            .Select(cycle =>
            {
                var ordered = cycle.OrderBy(projectId => projectId, StringComparer.Ordinal).ToArray();
                var containsTest = ordered.Any(projectId => testProjectIds.Contains(projectId));
                var totalFanOut = ordered.Sum(projectId => fanOutByProject[projectId]);
                var score = (ordered.Length * 100) + (containsTest ? 35 : 0) + (totalFanOut * 5);
                return new InsightsCycleMetric(ordered, ordered.Length, containsTest, totalFanOut, score);
            })
            .OrderByDescending(cycle => cycle.SeverityScore)
            .ThenByDescending(cycle => cycle.Length)
            .ThenBy(cycle => cycle.ProjectIds[0], StringComparer.Ordinal)
            .ToArray();

        var namespaces = symbolData.Namespaces
            .SelectMany(project => project.Namespaces)
            .GroupBy(ns => ns.Name, StringComparer.Ordinal)
            .Select(group =>
            {
                var publicTypeCount = group.Sum(item => item.PublicTypeCount);
                var publicMethodCount = group.Sum(item => item.PublicMethodCount);
                var totalMethodCount = group.Sum(item => item.TotalMethodCount);
                var score = (publicMethodCount * 4) + (publicTypeCount * 2) + totalMethodCount;
                return new InsightsNamespaceMetric(group.Key, publicTypeCount, publicMethodCount, totalMethodCount, score);
            })
            .Where(metric => !string.IsNullOrWhiteSpace(metric.Namespace))
            .OrderByDescending(metric => metric.Score)
            .ThenBy(metric => metric.Namespace, StringComparer.Ordinal)
            .Take(10)
            .ToArray();

        var packageDriftHotspots = BuildPackageDriftHotspots(packages);

        return new InsightsReportData(
            "insights",
            context.SolutionPath,
            context.AnalysisVersion,
            topFanIn,
            topFanOut,
            coreProjects,
            riskyProjects,
            cycleSeverity,
            namespaces,
            packageDriftHotspots,
            Rules);
    }

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var hashService = new DocumentHashService(fileSystem);
        var cacheStore = new FileCacheStore(fileSystem, hashService, context.CacheDir);
        var cache = new DocumentCache(cacheStore);
        var index = new SymbolIndex(new DocumentFilter(), hashService, cache, context.MaxDegreeOfParallelism);
        var symbolData = await index.BuildAsync(context.Solution, context.AnalysisVersion, cancellationToken, context.RepoRootPath);
        var data = Create(context, symbolData);

        var jsonPath = Path.Combine(outputDirectory, "insights.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await fileSystem.WriteAllTextAsync(jsonPath, json, cancellationToken);

        var markdownPath = Path.Combine(outputDirectory, "insights.md");
        var markdown = BuildMarkdown(data);
        await fileSystem.WriteAllTextAsync(markdownPath, markdown, cancellationToken);
    }

    public static string BuildMarkdown(InsightsReportData data)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Insights");
        builder.AppendLine();
        builder.AppendLine($"- Solution: {data.SolutionPath}");
        builder.AppendLine($"- Analysis version: {data.AnalysisVersion}");
        builder.AppendLine();
        AppendProjectSection(builder, "Top fan-in projects", data.TopFanInProjects);
        AppendProjectSection(builder, "Top fan-out projects", data.TopFanOutProjects);
        AppendProjectSection(builder, "Core projects", data.CoreProjects);
        AppendProjectSection(builder, "Risky projects", data.RiskyProjects);
        return builder.ToString();
    }

    private static HashSet<string> ResolveTestProjects(PackagesReportData packages, ProjectGraph graph)
    {
        var graphIds = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var testProjects = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in packages.Projects)
        {
            var isTest = project.ProjectName.Contains("test", StringComparison.OrdinalIgnoreCase)
                         || project.PackageReferences.Any(reference =>
                             TestPackageMarkers.Any(marker => reference.Id.Contains(marker, StringComparison.OrdinalIgnoreCase)));

            if (isTest && graphIds.Contains(project.ProjectId))
            {
                testProjects.Add(project.ProjectId);
            }
        }

        return testProjects;
    }

    private static IReadOnlyList<InsightsPackageDriftHotspot> BuildPackageDriftHotspots(PackagesReportData packages)
    {
        var packageRefs = packages.Projects
            .SelectMany(project => project.PackageReferences.Select(reference => new
            {
                reference.Id,
                Version = reference.Version ?? string.Empty,
                project.ProjectId
            }))
            .Where(reference => !string.IsNullOrWhiteSpace(reference.Id))
            .ToArray();

        return packageRefs
            .GroupBy(reference => reference.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                var versions = group.Select(reference => reference.Version)
                    .Where(version => !string.IsNullOrWhiteSpace(version))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(version => version, StringComparer.Ordinal)
                    .ToArray();

                var majorSet = versions
                    .Select(TryParseMajorVersion)
                    .Where(major => major.HasValue)
                    .Select(major => major!.Value)
                    .Distinct()
                    .ToArray();

                var hasPrerelease = versions.Any(IsPrerelease);
                var projectCount = group.Select(reference => reference.ProjectId).Distinct(StringComparer.Ordinal).Count();
                var score = (majorSet.Length * 50) + (hasPrerelease ? 25 : 0) + (projectCount * 2);

                return new InsightsPackageDriftHotspot(
                    group.Key,
                    versions,
                    majorSet.Length,
                    hasPrerelease,
                    projectCount,
                    score);
            })
            .Where(hotspot => hotspot.DistinctMajorCount > 1 || hotspot.HasPrerelease)
            .OrderByDescending(hotspot => hotspot.Score)
            .ThenBy(hotspot => hotspot.PackageId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int? TryParseMajorVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var cleaned = version.Trim();
        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex >= 0)
        {
            cleaned = cleaned[..dashIndex];
        }

        var dotIndex = cleaned.IndexOf('.');
        var majorPart = dotIndex >= 0 ? cleaned[..dotIndex] : cleaned;
        return int.TryParse(majorPart, out var major) ? major : null;
    }

    private static bool IsPrerelease(string version)
    {
        return version.Contains('-', StringComparison.Ordinal);
    }

    private static void AppendProjectSection(StringBuilder builder, string title, IReadOnlyList<InsightsProjectMetric> values)
    {
        builder.AppendLine($"## {title}");
        if (values.Count == 0)
        {
            builder.AppendLine("- (none)");
            builder.AppendLine();
            return;
        }

        foreach (var value in values)
        {
            builder.AppendLine($"- {value.ProjectName} ({value.ProjectId}) — fan-in {value.FanIn}, fan-out {value.FanOut}, score {value.Score}");
        }

        builder.AppendLine();
    }
}
