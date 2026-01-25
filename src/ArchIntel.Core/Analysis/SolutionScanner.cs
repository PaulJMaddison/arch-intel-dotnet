using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public sealed class SolutionScanner
{
    private readonly DocumentFilter _filter;
    private readonly DocumentHashService _hashService;
    private readonly DocumentCache _cache;
    private readonly int _maxDegreeOfParallelism;

    public SolutionScanner(
        DocumentFilter filter,
        DocumentHashService hashService,
        DocumentCache cache,
        int maxDegreeOfParallelism)
    {
        _filter = filter;
        _hashService = hashService;
        _cache = cache;
        _maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism);
    }

    public async Task<ScanSummaryData> ScanAsync(AnalysisContext context, CancellationToken cancellationToken)
    {
        var analyzed = 0;
        var hits = 0;
        var misses = 0;

        var documents = context.Solution.Projects
            .SelectMany(project => project.Documents.Select(document => (Project: project, Document: document)));

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(documents, options, async (item, token) =>
        {
            var document = item.Document;
            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                return;
            }

            if (_filter.IsExcluded(document.FilePath))
            {
                return;
            }

            Interlocked.Increment(ref analyzed);
            var text = await document.GetTextAsync(token);
            var contentHash = _hashService.GetContentHash(text.ToString());
            var key = new CacheKey(
                context.AnalysisVersion,
                item.Project.Id.Id.ToString(),
                Path.GetFullPath(document.FilePath),
                contentHash);

            var status = await _cache.GetStatusAsync(key, token);
            if (status == CacheStatus.Hit)
            {
                Interlocked.Increment(ref hits);
            }
            else
            {
                Interlocked.Increment(ref misses);
            }
        });

        return new ScanSummaryData(
            analyzed,
            hits,
            misses);
    }
}

public sealed record ScanCounts(int ProjectCount, int FailedProjectCount, int AnalyzedDocuments);
public sealed record ScanSummaryData(int AnalyzedDocuments, int CacheHits, int CacheMisses);
