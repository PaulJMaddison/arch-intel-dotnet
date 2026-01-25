namespace ArchIntel.Analysis;

public interface ICacheStore
{
    Task<CacheEntry?> TryGetAsync(CacheKey key, CancellationToken cancellationToken);
    Task StoreAsync(CacheEntry entry, CancellationToken cancellationToken);
}
