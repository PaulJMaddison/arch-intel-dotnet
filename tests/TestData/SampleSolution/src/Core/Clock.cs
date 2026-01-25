namespace SampleSolution.Core;

public sealed class Clock
{
    public DateTimeOffset Now() => DateTimeOffset.UtcNow;
}
