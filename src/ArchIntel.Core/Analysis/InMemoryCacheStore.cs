namespace ArchIntel.Analysis;

public sealed class InMemoryCacheStore : ICacheStore
{
    private readonly Dictionary<CacheKey, CacheEntry> _entries = new();

    public Task<CacheEntry?> TryGetAsync(CacheKey key, CancellationToken cancellationToken)
    {
        _entries.TryGetValue(key, out var entry);
        return Task.FromResult(entry);
    }

    public Task StoreAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        _entries[entry.Key] = entry;
        return Task.CompletedTask;
    }
}
