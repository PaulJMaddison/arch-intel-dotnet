using ArchIntel.Analysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class CacheTests
{
    [Fact]
    public async Task DocumentCache_ReturnsHitAfterStore()
    {
        var cacheStore = new InMemoryCacheStore();
        var cache = new DocumentCache(cacheStore);
        var key = new CacheKey("1", "project-a", "/repo/src/File.cs", "hash-1");

        var first = await cache.GetStatusAsync(key, CancellationToken.None);
        var second = await cache.GetStatusAsync(key, CancellationToken.None);

        Assert.Equal(CacheStatus.Miss, first);
        Assert.Equal(CacheStatus.Hit, second);
    }

    [Fact]
    public async Task DocumentCache_InvalidatesWhenAnalysisVersionChanges()
    {
        var cacheStore = new InMemoryCacheStore();
        var cache = new DocumentCache(cacheStore);
        var keyV1 = new CacheKey("1", "project-a", "/repo/src/File.cs", "hash-1");
        var keyV2 = new CacheKey("2", "project-a", "/repo/src/File.cs", "hash-1");

        var first = await cache.GetStatusAsync(keyV1, CancellationToken.None);
        var second = await cache.GetStatusAsync(keyV2, CancellationToken.None);

        Assert.Equal(CacheStatus.Miss, first);
        Assert.Equal(CacheStatus.Miss, second);
    }
}
