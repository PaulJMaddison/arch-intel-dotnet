using System.Text.Json;

namespace ArchIntel.Configuration;

public sealed class AnalysisConfig
{
    public static readonly IReadOnlyList<string> DefaultIncludeGlobs = ["**/*.cs", "**/*.csproj", "**/*.sln", "**/*.props", "**/*.targets"];
    public static readonly IReadOnlyList<string> DefaultExcludeGlobs = ["**/bin/**", "**/obj/**", "**/.git/**", "**/.archintel/**"];

    public IReadOnlyList<string> IncludeGlobs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = Array.Empty<string>();
    public string? OutputDir { get; init; }
    public string? CacheDir { get; init; }
    public int? MaxDegreeOfParallelism { get; init; }
    public bool FailOnLoadIssues { get; init; }
    public bool IncludeDocSnippets { get; init; }
    public StrictModeConfig Strict { get; init; } = new();
    public ArchitectureRulesConfig ArchitectureRules { get; init; } = new();
    public LayerClassificationConfig Layers { get; init; } = new();

    public IReadOnlyList<string> GetEffectiveIncludeGlobs()
    {
        return IncludeGlobs.Count == 0 ? DefaultIncludeGlobs : IncludeGlobs;
    }

    public IReadOnlyList<string> GetEffectiveExcludeGlobs()
    {
        return ExcludeGlobs.Count == 0 ? DefaultExcludeGlobs : ExcludeGlobs;
    }

    public int GetEffectiveMaxDegreeOfParallelism()
    {
        var fallback = Math.Max(1, Environment.ProcessorCount - 1);
        if (MaxDegreeOfParallelism is null)
        {
            return fallback;
        }

        return Math.Max(1, MaxDegreeOfParallelism.Value);
    }

    public static AnalysisConfig Load(string? configPath)
    {
        var path = ResolveConfigPath(configPath);
        if (path is null || !File.Exists(path))
        {
            return new AnalysisConfig();
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<AnalysisConfig>(json, options) ?? new AnalysisConfig();
    }

    private static string? ResolveConfigPath(string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            return Path.GetFullPath(configPath);
        }

        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), ".archintel", "config.json");
        return defaultPath;
    }
}
