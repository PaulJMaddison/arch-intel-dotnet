namespace ArchIntel.Analysis;

public enum CacheStatus
{
    Hit,
    Miss
}

public sealed class DocumentCache
{
    private readonly ICacheStore _cacheStore;

    public DocumentCache(ICacheStore cacheStore)
    {
        _cacheStore = cacheStore;
    }

    public async Task<CacheStatus> GetStatusAsync(CacheKey key, CancellationToken cancellationToken)
    {
        var entry = await _cacheStore.TryGetAsync(key, cancellationToken);
        if (entry is not null && entry.Key == key)
        {
            return CacheStatus.Hit;
        }

        await _cacheStore.StoreAsync(new CacheEntry(key, DateTimeOffset.UtcNow), cancellationToken);
        return CacheStatus.Miss;
    }
}
