using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace ArchIntel.Analysis;

public sealed record ImpactDefinitionLocation(string FilePath, int Line, int Column);

public sealed record ImpactAnalysisResult(
    string Symbol,
    bool Found,
    ImpactDefinitionLocation? DefinitionLocation,
    IReadOnlyList<string> ImpactedProjects,
    IReadOnlyList<string> ImpactedFiles,
    int TotalReferences,
    IReadOnlyList<string> Suggestions)
{
    public static ImpactAnalysisResult NotFound(string symbol, IReadOnlyList<string> suggestions)
    {
        return new ImpactAnalysisResult(
            symbol,
            false,
            null,
            Array.Empty<string>(),
            Array.Empty<string>(),
            0,
            suggestions);
    }
}

public sealed class ImpactAnalysisEngine
{
    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.None,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.None,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    private readonly DocumentFilter _filter;
    private readonly SymbolIndex _symbolIndex;

    public ImpactAnalysisEngine(
        DocumentFilter filter,
        DocumentHashService hashService,
        DocumentCache cache,
        int maxDegreeOfParallelism)
    {
        _filter = filter;
        _symbolIndex = new SymbolIndex(filter, hashService, cache, maxDegreeOfParallelism);
    }

    public async Task<ImpactAnalysisResult> AnalyzeAsync(
        Solution solution,
        string analysisVersion,
        string symbolName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbolName))
        {
            throw new ArgumentException("Symbol name is required.", nameof(symbolName));
        }

        var normalizedSymbolName = NormalizeSymbolName(symbolName);
        var indexData = await _symbolIndex.BuildAsync(solution, analysisVersion, cancellationToken);
        var candidates = indexData.Symbols
            .Select(entry => new SymbolCandidate(entry, BuildQualifiedName(entry)))
            .ToArray();

        if (!candidates.Any(candidate => string.Equals(candidate.QualifiedName, normalizedSymbolName, StringComparison.Ordinal)))
        {
            var suggestions = BuildSuggestions(normalizedSymbolName, candidates.Select(candidate => candidate.QualifiedName));
            return ImpactAnalysisResult.NotFound(symbolName, suggestions);
        }

        var simpleName = ExtractSimpleName(normalizedSymbolName);
        var declarationsByProject = await Task.WhenAll(solution.Projects.Select(project =>
            SymbolFinder.FindDeclarationsAsync(project, simpleName, ignoreCase: false, cancellationToken)));
        var matchingSymbols = declarationsByProject
            .SelectMany(declarations => declarations)
            .Where(symbol => string.Equals(GetQualifiedName(symbol), normalizedSymbolName, StringComparison.Ordinal))
            .ToArray();

        if (matchingSymbols.Length == 0)
        {
            var suggestions = BuildSuggestions(normalizedSymbolName, candidates.Select(candidate => candidate.QualifiedName));
            return ImpactAnalysisResult.NotFound(symbolName, suggestions);
        }

        var referenceLocations = new List<ReferenceLocationInfo>();

        foreach (var symbol in matchingSymbols)
        {
            var referenceGroups = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);
            foreach (var referenceGroup in referenceGroups)
            {
                foreach (var location in referenceGroup.Locations)
                {
                    var document = location.Document;
                    var filePath = document.FilePath;
                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        continue;
                    }

                    if (_filter.IsExcluded(filePath))
                    {
                        continue;
                    }

                    var lineSpan = location.Location.GetLineSpan();
                    referenceLocations.Add(new ReferenceLocationInfo(
                        document.Project.Name,
                        Path.GetFullPath(filePath),
                        lineSpan.StartLinePosition.Line + 1,
                        lineSpan.StartLinePosition.Character + 1));
                }
            }
        }

        var impactedProjects = referenceLocations
            .Select(reference => reference.ProjectName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(project => project, StringComparer.Ordinal)
            .ToArray();
        var impactedFiles = referenceLocations
            .Select(reference => reference.FilePath)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var totalReferences = referenceLocations.Count;

        var definitionLocation = matchingSymbols
            .SelectMany(symbol => symbol.Locations)
            .Where(location => location.IsInSource)
            .Select(location => new
            {
                FilePath = location.SourceTree?.FilePath,
                LineSpan = location.GetLineSpan().StartLinePosition
            })
            .Where(location => !string.IsNullOrWhiteSpace(location.FilePath))
            .Select(location => new ImpactDefinitionLocation(
                Path.GetFullPath(location.FilePath!),
                location.LineSpan.Line + 1,
                location.LineSpan.Character + 1))
            .OrderBy(location => location.FilePath, StringComparer.Ordinal)
            .ThenBy(location => location.Line)
            .ThenBy(location => location.Column)
            .FirstOrDefault();

        return new ImpactAnalysisResult(
            symbolName,
            true,
            definitionLocation,
            impactedProjects,
            impactedFiles,
            totalReferences,
            Array.Empty<string>());
    }

    private static string NormalizeSymbolName(string symbolName)
    {
        var trimmed = symbolName.Trim();
        return trimmed.StartsWith("global::", StringComparison.Ordinal)
            ? trimmed["global::".Length..]
            : trimmed;
    }

    private static string BuildQualifiedName(SymbolIndexEntry entry)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(entry.Namespace))
        {
            parts.Add(entry.Namespace!);
        }

        if (!string.IsNullOrWhiteSpace(entry.ContainingType))
        {
            parts.Add(entry.ContainingType!);
        }

        parts.Add(entry.Name);
        return string.Join(".", parts);
    }

    private static string GetQualifiedName(ISymbol symbol)
    {
        return symbol.ToDisplayString(QualifiedNameFormat);
    }

    private static string ExtractSimpleName(string qualifiedName)
    {
        var lastDot = qualifiedName.LastIndexOf('.', qualifiedName.Length - 1);
        return lastDot >= 0 ? qualifiedName[(lastDot + 1)..] : qualifiedName;
    }

    private static IReadOnlyList<string> BuildSuggestions(string symbolName, IEnumerable<string> candidates)
    {
        var normalized = symbolName.Trim();
        var normalizedLower = normalized.ToLowerInvariant();
        var possible = candidates
            .Distinct(StringComparer.Ordinal)
            .Select(candidate => new
            {
                Name = candidate,
                Lower = candidate.ToLowerInvariant(),
                Distance = ComputeDistance(normalizedLower, candidate.ToLowerInvariant())
            })
            .Where(candidate => candidate.Lower.Contains(normalizedLower, StringComparison.Ordinal)
                || normalizedLower.Contains(candidate.Lower, StringComparison.Ordinal)
                || candidate.Distance <= Math.Max(2, normalized.Length / 3))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(5)
            .Select(candidate => candidate.Name)
            .ToArray();

        return possible;
    }

    private static int ComputeDistance(string source, string target)
    {
        if (string.Equals(source, target, StringComparison.Ordinal))
        {
            return 0;
        }

        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        var distances = new int[source.Length + 1, target.Length + 1];
        for (var i = 0; i <= source.Length; i++)
        {
            distances[i, 0] = i;
        }

        for (var j = 0; j <= target.Length; j++)
        {
            distances[0, j] = j;
        }

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                distances[i, j] = Math.Min(
                    Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                    distances[i - 1, j - 1] + cost);
            }
        }

        return distances[source.Length, target.Length];
    }

    private sealed record SymbolCandidate(SymbolIndexEntry Entry, string QualifiedName);

    private sealed record ReferenceLocationInfo(string ProjectName, string FilePath, int Line, int Column);
}
