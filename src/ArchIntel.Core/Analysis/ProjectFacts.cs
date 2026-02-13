using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ArchIntel.Configuration;
using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public enum TestDetectionReason
{
    Property,
    MicrosoftNetTestSdk,
    TestFrameworkPackage,
    NamePattern,
    DefaultFalse
}

public enum LayerClassificationReason
{
    Rule,
    HeuristicIsTestProject,
    HeuristicAspNetPackage,
    HeuristicDataPackage,
    HeuristicDomainName,
    HeuristicApplicationName,
    DefaultUnknown
}

public sealed record ProjectFactsResult(
    string ProjectId,
    string RoslynProjectId,
    bool IsTestProject,
    TestDetectionReason TestDetectionReason,
    string Layer,
    LayerClassificationReason LayerReason,
    string? LayerRuleMatched,
    IReadOnlyList<string> PackageReferences);

public static class ProjectFacts
{
    private static readonly string[] TestNamePatterns = [".Tests", ".UnitTests", ".IntegrationTests", ".E2E", ".UITests"];
    private static readonly string[] StrongTestFrameworkPackages = ["xunit", "nunit", "mstest.testframework", "mstest.sdk", "coverlet.collector"];
    private static readonly string[] AspNetPackages = ["microsoft.aspnetcore"];
    private static readonly string[] DataPackages = ["microsoft.entityframeworkcore", "dapper"];
    private static readonly Regex GlobTokenRegex = new("([.^$+{}()|\\[\\]\\\\])", RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, CsprojMetadata> CsprojCache = new(StringComparer.OrdinalIgnoreCase);

    public static ProjectFactsResult Get(Project project, string repoRootPath, AnalysisConfig config)
    {
        var projectId = ProjectIdentity.CreateStableId(project, repoRootPath);
        var roslynProjectId = project.Id.Id.ToString();
        var metadata = ReadMetadata(project.FilePath);

        var (isTestProject, testReason) = DetectTestProject(project.Name, metadata);
        var (layer, layerReason, matchedRule) = ClassifyLayer(project.Name, isTestProject, metadata.PackageReferences, config.Layers);

        return new ProjectFactsResult(
            projectId,
            roslynProjectId,
            isTestProject,
            testReason,
            layer,
            layerReason,
            matchedRule,
            metadata.PackageReferences);
    }

    private static (bool IsTestProject, TestDetectionReason Reason) DetectTestProject(string projectName, CsprojMetadata metadata)
    {
        if (metadata.IsTestProjectProperty)
        {
            return (true, TestDetectionReason.Property);
        }

        var hasMicrosoftNetTestSdk = metadata.PackageReferences.Any(p => string.Equals(p, "microsoft.net.test.sdk", StringComparison.OrdinalIgnoreCase));
        if (hasMicrosoftNetTestSdk)
        {
            return (true, TestDetectionReason.MicrosoftNetTestSdk);
        }

        if (metadata.PackageReferences.Any(IsStrongTestFrameworkPackage))
        {
            return (true, TestDetectionReason.TestFrameworkPackage);
        }

        if (TestNamePatterns.Any(pattern => projectName.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
        {
            return (true, TestDetectionReason.NamePattern);
        }

        return (false, TestDetectionReason.DefaultFalse);
    }

    private static (string Layer, LayerClassificationReason Reason, string? RuleMatched) ClassifyLayer(
        string projectName,
        bool isTestProject,
        IReadOnlyList<string> packageReferences,
        LayerClassificationConfig config)
    {
        // Built-in deterministic name rules (ordered, tests first).
        if (projectName.Contains(".Tests", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".UnitTests", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".IntegrationTests", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".E2E", StringComparison.OrdinalIgnoreCase))
        {
            return ("Tests", LayerClassificationReason.Rule, "builtin:.Tests");
        }

        if (projectName.Contains(".Web", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Api", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Site", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Ui", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".UI", StringComparison.OrdinalIgnoreCase))
        {
            return ("Presentation", LayerClassificationReason.Rule, "builtin:.Api");
        }

        if (projectName.Contains(".Infrastructure", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Data", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Persistence", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Rag", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".AI", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".McpServer", StringComparison.OrdinalIgnoreCase))
        {
            return ("Infrastructure", LayerClassificationReason.Rule, "builtin:.Infrastructure");
        }

        if (projectName.Contains(".Application", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".UseCases", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Services", StringComparison.OrdinalIgnoreCase))
        {
            return ("Application", LayerClassificationReason.Rule, "builtin:.Application");
        }

        if (projectName.Contains(".Domain", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Core", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains(".Model", StringComparison.OrdinalIgnoreCase))
        {
            return ("Domain", LayerClassificationReason.Rule, "builtin:.Domain");
        }

        var configuredRules = (config.Rules.Count == 0 ? new LayerClassificationConfig().Rules : config.Rules);
        foreach (var rule in configuredRules)
        {
            foreach (var pattern in rule.ProjectNamePatterns)
            {
                if (GlobMatch(projectName, pattern))
                {
                    return (rule.Layer, LayerClassificationReason.Rule, pattern);
                }
            }
        }

        if (isTestProject)
        {
            return ("Tests", LayerClassificationReason.HeuristicIsTestProject, null);
        }

        if (packageReferences.Any(p => AspNetPackages.Any(marker => p.Contains(marker, StringComparison.OrdinalIgnoreCase))))
        {
            return ("Presentation", LayerClassificationReason.HeuristicAspNetPackage, null);
        }

        if (packageReferences.Any(p => DataPackages.Any(marker => p.Contains(marker, StringComparison.OrdinalIgnoreCase))))
        {
            return ("Infrastructure", LayerClassificationReason.HeuristicDataPackage, null);
        }

        if (projectName.Contains("Domain", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("Model", StringComparison.OrdinalIgnoreCase))
        {
            return ("Domain", LayerClassificationReason.HeuristicDomainName, null);
        }

        if (projectName.Contains("Application", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("UseCase", StringComparison.OrdinalIgnoreCase) ||
            projectName.Contains("Service", StringComparison.OrdinalIgnoreCase))
        {
            return ("Application", LayerClassificationReason.HeuristicApplicationName, null);
        }

        return (config.DefaultLayer, LayerClassificationReason.DefaultUnknown, null);
    }

    private static bool GlobMatch(string input, string pattern)
    {
        var escaped = GlobTokenRegex.Replace(pattern, "\\$1")
            .Replace("*", ".*")
            .Replace("?", ".");
        return Regex.IsMatch(input, $"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsStrongTestFrameworkPackage(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return false;
        }

        return StrongTestFrameworkPackages.Any(marker =>
            string.Equals(packageId, marker, StringComparison.OrdinalIgnoreCase)
            || packageId.StartsWith($"{marker}.", StringComparison.OrdinalIgnoreCase));
    }

    private static CsprojMetadata ReadMetadata(string? projectFilePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath) || !File.Exists(projectFilePath))
        {
            return CsprojMetadata.Empty;
        }

        if (CsprojCache.TryGetValue(projectFilePath, out var cached))
        {
            return cached;
        }

        try
        {
            var document = XDocument.Load(projectFilePath);
            var isTestProjectValue = document.Descendants()
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "IsTestProject", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            var isTestProperty = bool.TryParse(isTestProjectValue, out var parsed) && parsed;

            var packages = document.Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase))?.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToArray();

            var metadata = new CsprojMetadata(isTestProperty, packages);
            CsprojCache[projectFilePath] = metadata;
            return metadata;
        }
        catch
        {
            CsprojCache[projectFilePath] = CsprojMetadata.Empty;
            return CsprojMetadata.Empty;
        }
    }

    private sealed record CsprojMetadata(bool IsTestProjectProperty, IReadOnlyList<string> PackageReferences)
    {
        public static readonly CsprojMetadata Empty = new(false, Array.Empty<string>());
    }
}
