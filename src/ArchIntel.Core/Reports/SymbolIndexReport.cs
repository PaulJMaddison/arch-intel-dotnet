using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public static class SymbolIndexReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var hashService = new DocumentHashService(fileSystem);
        var cacheStore = new FileCacheStore(fileSystem, hashService, context.CacheDir);
        var cache = new DocumentCache(cacheStore);
        var index = new SymbolIndex(new DocumentFilter(), hashService, cache, context.MaxDegreeOfParallelism);

        var data = context.PipelineTimer is null
            ? await index.BuildAsync(context.Solution, context.AnalysisVersion, cancellationToken)
            : await context.PipelineTimer.TimeIndexSymbolsAsync(
                () => index.BuildAsync(context.Solution, context.AnalysisVersion, cancellationToken));

        var symbolsPath = Path.Combine(outputDirectory, "symbols.json");
        var symbolsJson = JsonSerializer.Serialize(data.Symbols, JsonOptions);
        await fileSystem.WriteAllTextAsync(symbolsPath, symbolsJson, cancellationToken);

        var namespacesPath = Path.Combine(outputDirectory, "namespaces.json");
        var namespacesJson = JsonSerializer.Serialize(data.Namespaces, JsonOptions);
        await fileSystem.WriteAllTextAsync(namespacesPath, namespacesJson, cancellationToken);
    }
}
