using ArchIntel.Analysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class CanonicalPathTests
{
    [Fact]
    public void Normalize_HandlesSlashDifferences_Deterministically()
    {
        var windows = CanonicalPath.Normalize("src\\A\\B.csproj", "/repo", windowsMode: false);
        var unix = CanonicalPath.Normalize("src/A/B.csproj", "/repo", windowsMode: false);

        Assert.Equal("src/A/B.csproj", windows);
        Assert.Equal(unix, windows);
    }

    [Fact]
    public void Normalize_LowersCaseInWindowsMode()
    {
        var normalized = CanonicalPath.Normalize("SRC/A/B.csproj", "/repo", windowsMode: true);

        Assert.Equal("src/a/b.csproj", normalized);
    }
}
