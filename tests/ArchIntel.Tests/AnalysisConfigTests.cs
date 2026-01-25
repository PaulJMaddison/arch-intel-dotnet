using ArchIntel.Configuration;
using Xunit;

namespace ArchIntel.Tests;

public class AnalysisConfigTests
{
    [Fact]
    public void GetEffectiveMaxDegreeOfParallelism_DefaultsToAtLeastOne()
    {
        var config = new AnalysisConfig { MaxDegreeOfParallelism = 0 };

        var value = config.GetEffectiveMaxDegreeOfParallelism();

        Assert.True(value >= 1);
    }

    [Fact]
    public void GetEffectiveMaxDegreeOfParallelism_WhenUnset_UsesCpuMinusOne()
    {
        var config = new AnalysisConfig();

        var value = config.GetEffectiveMaxDegreeOfParallelism();

        var expected = Math.Max(1, Environment.ProcessorCount - 1);
        Assert.Equal(expected, value);
    }
}
