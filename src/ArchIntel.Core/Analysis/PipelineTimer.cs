using System.Diagnostics;

namespace ArchIntel.Analysis;

public sealed class PipelineTimer
{
    public long LoadSolutionMs { get; private set; }
    public long BuildProjectGraphMs { get; private set; }
    public long IndexSymbolsMs { get; private set; }
    public long WriteReportsMs { get; private set; }

    public async Task<T> TimeLoadSolutionAsync<T>(Func<Task<T>> action)
    {
        return await TimeAsync(action, value => LoadSolutionMs = value);
    }

    public T TimeBuildProjectGraph<T>(Func<T> action)
    {
        return Time(action, value => BuildProjectGraphMs = value);
    }

    public async Task<T> TimeIndexSymbolsAsync<T>(Func<Task<T>> action)
    {
        return await TimeAsync(action, value => IndexSymbolsMs = value);
    }

    public async Task TimeWriteReportsAsync(Func<Task> action)
    {
        await TimeAsync(async () =>
        {
            await action();
            return 0;
        }, value => WriteReportsMs = value);
    }

    public PipelineTiming ToTiming() =>
        new(LoadSolutionMs, BuildProjectGraphMs, IndexSymbolsMs, WriteReportsMs);

    private static T Time<T>(Func<T> action, Action<long> assign)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();
        assign(stopwatch.ElapsedMilliseconds);
        return result;
    }

    private static async Task<T> TimeAsync<T>(Func<Task<T>> action, Action<long> assign)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await action();
        stopwatch.Stop();
        assign(stopwatch.ElapsedMilliseconds);
        return result;
    }
}

public sealed record PipelineTiming(
    long LoadSolutionMs,
    long BuildProjectGraphMs,
    long IndexSymbolsMs,
    long WriteReportsMs);
