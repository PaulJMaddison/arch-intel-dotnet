using System.Text;

namespace ArchIntel.Analysis;

public static class ProjectLayerClassifier
{
    private static readonly (string Layer, string[] Tokens)[] LayerTokens =
    [
        ("Tests", ["test", "tests", "spec", "specs"]),
        ("Presentation", ["api", "web", "ui", "presentation", "frontend", "client"]),
        ("Application", ["application", "service", "services", "usecase", "usecases"]),
        ("Domain", ["domain", "core", "model", "models"]),
        ("Infrastructure", ["infrastructure", "infra", "data", "persistence", "repository", "repositories", "storage"])
    ];

    public static string Classify(string name, string path)
    {
        var tokens = new HashSet<string>(Tokenize(name).Concat(Tokenize(path)), StringComparer.Ordinal);
        foreach (var (layer, layerTokens) in LayerTokens)
        {
            foreach (var token in layerTokens)
            {
                if (tokens.Contains(token))
                {
                    return layer;
                }
            }
        }

        return "Unknown";
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (builder.Length > 0)
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }
}
