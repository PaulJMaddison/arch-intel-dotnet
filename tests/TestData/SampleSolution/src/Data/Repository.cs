using SampleSolution.Core;

namespace SampleSolution.Data;

public sealed class Repository
{
    private readonly Clock _clock;

    public Repository(Clock clock)
    {
        _clock = clock;
    }

    public string GetTimestamp() => _clock.Now().ToString("O");
}
