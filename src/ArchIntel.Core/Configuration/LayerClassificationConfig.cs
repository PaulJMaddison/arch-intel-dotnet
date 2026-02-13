namespace ArchIntel.Configuration;

public sealed class LayerClassificationConfig
{
    public IReadOnlyList<LayerRuleConfig> Rules { get; init; } =
    [
        new() { Layer = "Tests", ProjectNamePatterns = [".Tests", ".UnitTests", ".IntegrationTests", ".E2E"] },
        new() { Layer = "Presentation", ProjectNamePatterns = [".Web", ".Api", ".Site", ".Ui", ".UI"] },
        new() { Layer = "Infrastructure", ProjectNamePatterns = [".Infrastructure", ".Data", ".Persistence", ".Rag", ".AI", ".McpServer"] },
        new() { Layer = "Application", ProjectNamePatterns = [".Application", ".UseCases", ".Services"] },
        new() { Layer = "Domain", ProjectNamePatterns = [".Domain", ".Core", "*.Model"] }
    ];

    public string DefaultLayer { get; init; } = "Unknown";
}

public sealed class LayerRuleConfig
{
    public string Layer { get; init; } = string.Empty;
    public IReadOnlyList<string> ProjectNamePatterns { get; init; } = Array.Empty<string>();
}
