using System.Text.Json;
using ArchIntel.Analysis;
using ArchIntel.IO;

namespace ArchIntel.Reports;

public sealed record ScanSummaryReportData(
    ScanCounts Counts,
    int CacheHits,
    int CacheMisses,
    PipelineTiming Durations);

public static class ScanSummaryReport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<ScanSummaryReportData> CreateAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        CancellationToken cancellationToken)
    {
        var hashService = new DocumentHashService(fileSystem);
        var cacheStore = new FileCacheStore(fileSystem, hashService, context.CacheDir);
        var cache = new DocumentCache(cacheStore);
        var scanner = new SolutionScanner(new DocumentFilter(), hashService, cache);

        var scanData = context.PipelineTimer is null
            ? await scanner.ScanAsync(context, cancellationToken)
            : await context.PipelineTimer.TimeIndexSymbolsAsync(() => scanner.ScanAsync(context, cancellationToken));

        var timing = context.PipelineTimer?.ToTiming() ?? new PipelineTiming(0, 0, 0, 0);
        return new ScanSummaryReportData(scanData.Counts, scanData.CacheHits, scanData.CacheMisses, timing);
    }

    public static async Task WriteAsync(
        AnalysisContext context,
        IFileSystem fileSystem,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        var data = await CreateAsync(context, fileSystem, cancellationToken);
        var path = Path.Combine(outputDirectory, "scan_summary.json");
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await fileSystem.WriteAllTextAsync(path, json, cancellationToken);
    }
}
