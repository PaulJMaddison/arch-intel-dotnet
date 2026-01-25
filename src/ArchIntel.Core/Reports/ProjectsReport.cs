using System.Text.Json;
using System.Xml.Linq;
using ArchIntel.Analysis;
using ArchIntel.IO;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Reports;

public sealed record ProjectsReportProjectReference(string ProjectId, string ProjectName);

public sealed record ProjectsReportProject(
    string ProjectId,
    string ProjectName,
    string? AssemblyName,
    IReadOnlyList<string> TargetFrameworks,
    string? OutputType,
    bool IsTestProject,
    IReadOnlyList<ProjectsReportProjectReference> ProjectReferences);

public sealed record ProjectsReportGraphSummary(
    int TotalProjects,
    int TotalEdges,
    bool CyclesDetected,
    IReadOnlyList<IReadOnlyList<string>> StronglyConnectedComponents);

public sealed record ProjectsReportData(
    string Kind,
    string SolutionPath,
    string AnalysisVersion,
    IReadOnlyList<ProjectsReportProject> Projects,
    ProjectsReportGraphSummary Graph);

public static class ProjectsReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] TestPackageMarkers =
    [
        "xunit",
        "nunit",
        "mstest"
    ];

    public static ProjectsReportData Create(AnalysisContext context)
    {
        var solution = context.Solution;
        var graph = ProjectGraphBuilder.Build(solution, context.RepoRootPath);
        var idMap = solution.Projects.ToDictionary(project => project.Id, ProjectIdentity.CreateStableId);

        var projects = solution.Projects
            .Select(project => CreateProject(project, solution, idMap))
            .OrderBy(project => project.ProjectName, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectId, StringComparer.Ordinal)
            .ToArray();

        var summary = new ProjectsReportGraphSummary(
            graph.Nodes.Count,
            graph.Edges.Count,
            graph.Cycles.Count > 0,
            graph.Cycles);

        return new ProjectsReportData(
            "projects",
            context.SolutionPath,
            context.AnalysisVersion,
            projects,
            summary);
    }

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var data = Create(context);
        var path = Path.Combine(outputDirectory, "projects.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static ProjectsReportProject CreateProject(
        Project project,
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> idMap)
    {
        var projectId = idMap[project.Id];
        var references = project.ProjectReferences
            .Select(reference => solution.GetProject(reference.ProjectId))
            .Where(referenceProject => referenceProject is not null)
            .Select(referenceProject => new ProjectsReportProjectReference(
                idMap[referenceProject!.Id],
                referenceProject.Name))
            .OrderBy(reference => reference.ProjectName, StringComparer.Ordinal)
            .ThenBy(reference => reference.ProjectId, StringComparer.Ordinal)
            .ToArray();

        var assemblyName = string.IsNullOrWhiteSpace(project.AssemblyName)
            ? null
            : project.AssemblyName;

        var targetFrameworks = GetTargetFrameworks(project.FilePath);
        var outputType = GetOutputType(project);

        return new ProjectsReportProject(
            projectId,
            project.Name,
            assemblyName,
            targetFrameworks,
            outputType,
            IsTestProject(project),
            references);
    }

    private static IReadOnlyList<string> GetTargetFrameworks(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return Array.Empty<string>();
        }

        try
        {
            var document = XDocument.Load(filePath);
            var frameworks = GetFrameworkValue(document, "TargetFrameworks")
                             ?? GetFrameworkValue(document, "TargetFramework");
            if (string.IsNullOrWhiteSpace(frameworks))
            {
                return Array.Empty<string>();
            }

            return frameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? GetFrameworkValue(XDocument document, string elementName)
    {
        return document.Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? GetOutputType(Project project)
    {
        var outputKind = project.CompilationOptions?.OutputKind;
        return outputKind switch
        {
            OutputKind.ConsoleApplication => "Exe",
            OutputKind.WindowsApplication => "Exe",
            OutputKind.WindowsRuntimeApplication => "Exe",
            OutputKind.DynamicallyLinkedLibrary => "Library",
            OutputKind.NetModule => "Library",
            OutputKind.WindowsRuntimeMetadata => "Library",
            _ => null
        };
    }

    private static bool IsTestProject(Project project)
    {
        if (project.Name.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasTestPackageReference(project);
    }

    private static bool HasTestPackageReference(Project project)
    {
        foreach (var candidate in GetReferenceNames(project))
        {
            foreach (var marker in TestPackageMarkers)
            {
                if (candidate.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> GetReferenceNames(Project project)
    {
        foreach (var reference in project.MetadataReferences)
        {
            if (!string.IsNullOrWhiteSpace(reference.Display))
            {
                yield return reference.Display!;
            }
        }

        foreach (var analyzer in project.AnalyzerReferences)
        {
            if (!string.IsNullOrWhiteSpace(analyzer.FullPath))
            {
                yield return analyzer.FullPath;
            }
        }
    }
}
