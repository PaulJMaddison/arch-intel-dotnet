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
}
