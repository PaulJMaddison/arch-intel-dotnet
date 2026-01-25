namespace ArchIntel.Analysis;

public sealed class InMemoryCacheStore : ICacheStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CacheKey, CacheEntry> _entries = new();

    public Task<CacheEntry?> TryGetAsync(CacheKey key, CancellationToken cancellationToken)
    {
        return Task.FromResult(_entries.TryGetValue(key, out var entry) ? entry : null);
    }

    public Task StoreAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        _entries[entry.Key] = entry;
        return Task.CompletedTask;
    }
}
