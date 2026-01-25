using System.Text.Json;

namespace ArchIntel.Configuration;

public sealed class AnalysisConfig
{
    public IReadOnlyList<string> IncludeGlobs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludeGlobs { get; init; } = Array.Empty<string>();
    public string? OutputDir { get; init; }
    public string? CacheDir { get; init; }
    public int? MaxDegreeOfParallelism { get; init; }
    public ArchitectureRulesConfig ArchitectureRules { get; init; } = new();

    public int GetEffectiveMaxDegreeOfParallelism()
    {
        var fallback = Math.Max(1, Environment.ProcessorCount);
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

        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), ".archtool", "config.json");
        return defaultPath;
    }
}
