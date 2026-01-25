using System.Text.Json;
using System.Xml.Linq;
using ArchIntel.Analysis;
using ArchIntel.IO;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Reports;

public sealed record PackagesReportPackageReference(string Id, string? Version);

public sealed record PackagesReportFrameworkReference(string Id);

public sealed record PackagesReportProject(
    string ProjectId,
    string ProjectName,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<PackagesReportPackageReference> PackageReferences,
    IReadOnlyList<PackagesReportFrameworkReference> FrameworkReferences);

public sealed record PackagesReportData(
    string Kind,
    string SolutionPath,
    string AnalysisVersion,
    IReadOnlyList<PackagesReportProject> Projects);

public static class PackagesReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly PackagesReportSnapshot EmptySnapshot = new(
        Array.Empty<string>(),
        Array.Empty<PackagesReportPackageReference>(),
        Array.Empty<PackagesReportFrameworkReference>());

    public static PackagesReportData Create(AnalysisContext context)
    {
        var solution = context.Solution;
        var idMap = solution.Projects.ToDictionary(project => project.Id, ProjectIdentity.CreateStableId);
        var cache = new CsprojCache();

        var projects = solution.Projects
            .Select(project => CreateProject(project, idMap, cache))
            .OrderBy(project => project.ProjectName, StringComparer.Ordinal)
            .ThenBy(project => project.ProjectId, StringComparer.Ordinal)
            .ToArray();

        return new PackagesReportData(
            "packages",
            context.SolutionPath,
            context.AnalysisVersion,
            projects);
    }

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var data = Create(context);
        var path = Path.Combine(outputDirectory, "packages.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static PackagesReportProject CreateProject(
        Project project,
        IReadOnlyDictionary<ProjectId, string> idMap,
        CsprojCache cache)
    {
        var snapshot = cache.GetSnapshot(project.FilePath);

        return new PackagesReportProject(
            idMap[project.Id],
            project.Name,
            snapshot.TargetFrameworks,
            snapshot.PackageReferences,
            snapshot.FrameworkReferences);
    }

    private sealed record PackagesReportSnapshot(
        IReadOnlyList<string> TargetFrameworks,
        IReadOnlyList<PackagesReportPackageReference> PackageReferences,
        IReadOnlyList<PackagesReportFrameworkReference> FrameworkReferences);

    private sealed class CsprojCache
    {
        private readonly Dictionary<string, PackagesReportSnapshot> _cache = new(StringComparer.OrdinalIgnoreCase);

        public PackagesReportSnapshot GetSnapshot(string? filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return EmptySnapshot;
            }

            if (_cache.TryGetValue(filePath, out var cached))
            {
                return cached;
            }

            if (!File.Exists(filePath))
            {
                _cache[filePath] = EmptySnapshot;
                return EmptySnapshot;
            }

            try
            {
                var document = XDocument.Load(filePath);
                var snapshot = new PackagesReportSnapshot(
                    GetTargetFrameworks(document),
                    GetPackageReferences(document),
                    GetFrameworkReferences(document));
                _cache[filePath] = snapshot;
                return snapshot;
            }
            catch
            {
                _cache[filePath] = EmptySnapshot;
                return EmptySnapshot;
            }
        }

        private static IReadOnlyList<string> GetTargetFrameworks(XDocument document)
        {
            var frameworks = GetElementValue(document, "TargetFrameworks")
                             ?? GetElementValue(document, "TargetFramework");
            if (string.IsNullOrWhiteSpace(frameworks))
            {
                return Array.Empty<string>();
            }

            return frameworks
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<PackagesReportPackageReference> GetPackageReferences(XDocument document)
        {
            return document.Descendants()
                .Where(element => IsElementName(element, "PackageReference"))
                .Select(element =>
                {
                    var id = GetItemId(element);
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        return null;
                    }

                    var version = GetAttributeValue(element, "Version")
                                  ?? GetElementValue(element, "Version");
                    return new PackagesReportPackageReference(id, NormalizeValue(version));
                })
                .Where(reference => reference is not null)
                .Select(reference => reference!)
                .DistinctBy(reference => (reference.Id, reference.Version))
                .OrderBy(reference => reference.Id, StringComparer.Ordinal)
                .ThenBy(reference => reference.Version ?? string.Empty, StringComparer.Ordinal)
                .ToArray();
        }

        private static IReadOnlyList<PackagesReportFrameworkReference> GetFrameworkReferences(XDocument document)
        {
            return document.Descendants()
                .Where(element => IsElementName(element, "FrameworkReference"))
                .Select(element =>
                {
                    var id = GetItemId(element);
                    return string.IsNullOrWhiteSpace(id) ? null : new PackagesReportFrameworkReference(id);
                })
                .Where(reference => reference is not null)
                .Select(reference => reference!)
                .DistinctBy(reference => reference.Id, StringComparer.Ordinal)
                .OrderBy(reference => reference.Id, StringComparer.Ordinal)
                .ToArray();
        }

        private static string? GetItemId(XElement element)
        {
            return GetAttributeValue(element, "Include") ?? GetAttributeValue(element, "Update");
        }

        private static string? GetAttributeValue(XElement element, string name)
        {
            return element.Attributes()
                .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
                ?.Value;
        }

        private static string? GetElementValue(XElement element, string name)
        {
            return element.Elements()
                .FirstOrDefault(child => IsElementName(child, name))
                ?.Value;
        }

        private static string? GetElementValue(XDocument document, string name)
        {
            return document.Descendants()
                .FirstOrDefault(element => IsElementName(element, name))
                ?.Value;
        }

        private static bool IsElementName(XElement element, string name)
        {
            return string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeValue(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
