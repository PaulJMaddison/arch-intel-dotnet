using System.Text.RegularExpressions;

namespace ArchIntel.Reports;

public static partial class DeterministicRuleFormatter
{
    public static IReadOnlyList<string> SanitizeAndSort(IReadOnlyList<string> rules)
    {
        return rules
            .Select(Sanitize)
            .Where(rule => !string.IsNullOrWhiteSpace(rule))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(rule => rule, StringComparer.Ordinal)
            .ToArray();
    }

    public static string Sanitize(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return string.Empty;
        }

        var normalizedWhitespace = WhitespaceRegex().Replace(rule.Replace("\r", " ").Replace("\n", " "), " ");
        return normalizedWhitespace.Trim();
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}
