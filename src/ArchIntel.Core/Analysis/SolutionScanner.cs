using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public sealed class SolutionScanner
{
    private readonly DocumentFilter _filter;
    private readonly DocumentHashService _hashService;
    private readonly DocumentCache _cache;

    public SolutionScanner(DocumentFilter filter, DocumentHashService hashService, DocumentCache cache)
    {
        _filter = filter;
        _hashService = hashService;
        _cache = cache;
    }

    public async Task<ScanSummaryData> ScanAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var total = 0;
        var excluded = 0;
        var analyzed = 0;
        var hits = 0;
        var misses = 0;

        foreach (var project in context.Solution.Projects)
        {
            var projectId = project.Id.Id.ToString();
            foreach (var document in project.Documents)
            {
                if (string.IsNullOrWhiteSpace(document.FilePath))
                {
                    continue;
                }

                total += 1;
                if (_filter.IsExcluded(document.FilePath))
                {
                    excluded += 1;
                    continue;
                }

                analyzed += 1;
                var text = await document.GetTextAsync(cancellationToken);
                var contentHash = _hashService.GetContentHash(text.ToString());
                var key = new CacheKey(
                    context.AnalysisVersion,
                    projectId,
                    Path.GetFullPath(document.FilePath),
                    contentHash);

                var status = await _cache.GetStatusAsync(key, cancellationToken);
                if (status == CacheStatus.Hit)
                {
                    hits += 1;
                }
                else
                {
                    misses += 1;
                }
            }
        }

        return new ScanSummaryData(
            new ScanCounts(total, excluded, analyzed),
            hits,
            misses);
    }
}

public sealed record ScanCounts(int TotalDocuments, int ExcludedDocuments, int AnalyzedDocuments);
public sealed record ScanSummaryData(ScanCounts Counts, int CacheHits, int CacheMisses);
