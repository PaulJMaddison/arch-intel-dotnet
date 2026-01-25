using System.Text.Json;
using ArchIntel.IO;

namespace ArchIntel.Analysis;

public sealed class FileCacheStore : ICacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fileSystem;
    private readonly DocumentHashService _hashService;
    private readonly string _cacheDirectory;

    public FileCacheStore(IFileSystem fileSystem, DocumentHashService hashService, string cacheDirectory)
    {
        _fileSystem = fileSystem;
        _hashService = hashService;
        _cacheDirectory = cacheDirectory;
    }

    public async Task<CacheEntry?> TryGetAsync(CacheKey key, CancellationToken cancellationToken)
    {
        var path = GetCachePath(key);
        if (!_fileSystem.FileExists(path))
        {
            return null;
        }

        var json = await _fileSystem.ReadAllTextAsync(path, cancellationToken);
        var entry = JsonSerializer.Deserialize<CacheEntry>(json, JsonOptions);
        if (entry is null || entry.Key != key)
        {
            return null;
        }

        return entry;
    }

    public async Task StoreAsync(CacheEntry entry, CancellationToken cancellationToken)
    {
        _fileSystem.CreateDirectory(_cacheDirectory);
        var path = GetCachePath(entry.Key);
        var json = JsonSerializer.Serialize(entry, JsonOptions);
        await _fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }

    private string GetCachePath(CacheKey key)
    {
        var fileName = $"{_hashService.GetContentHash(key.ToCacheKeyString())}.json";
        return Path.Combine(_cacheDirectory, fileName);
    }
}
