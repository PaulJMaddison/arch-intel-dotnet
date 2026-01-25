using ArchIntel.IO;
using Xunit;

namespace ArchIntel.Tests;

public class PathsTests
{
    [Fact]
    public void GetReportsDirectory_UsesDefaultWhenNoOverride()
    {
        var baseDirectory = "/repo";

        var result = Paths.GetReportsDirectory(baseDirectory, null);

        Assert.Equal("/repo/.archintel", result.Replace('\\', '/'));
    }
}
