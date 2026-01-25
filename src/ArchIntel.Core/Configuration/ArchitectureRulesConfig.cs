namespace ArchIntel.Configuration;

public sealed class ArchitectureRulesConfig
{
    public bool UseDefaultLayerRules { get; init; } = true;
    public IReadOnlyList<LayerRule> LayerRules { get; init; } = Array.Empty<LayerRule>();
}

public sealed record LayerRule(string FromLayer, IReadOnlyList<string> AllowedLayers);
