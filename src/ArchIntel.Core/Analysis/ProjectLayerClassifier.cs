namespace ArchIntel.Analysis;

public static class ProjectLayerClassifier
{
    private static readonly (string Layer, string[] Tokens)[] LayerTokens =
    [
        ("Tests", ["test", "tests", "spec", "specs"]),
        ("Presentation", ["api", "web", "ui", "presentation", "frontend", "client"]),
        ("Application", ["application", "app", "service", "services", "usecase", "usecases"]),
        ("Domain", ["domain", "core", "model", "models"]),
        ("Infrastructure", ["infrastructure", "infra", "data", "persistence", "repository", "repositories", "storage"])
    ];

    public static string Classify(string name, string path)
    {
        var haystack = $"{name} {path}".ToLowerInvariant();
        foreach (var (layer, tokens) in LayerTokens)
        {
            foreach (var token in tokens)
            {
                if (haystack.Contains(token, StringComparison.Ordinal))
                {
                    return layer;
                }
            }
        }

        return "Unknown";
    }
}
