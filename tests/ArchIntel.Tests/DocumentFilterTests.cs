using ArchIntel.Analysis;
using Xunit;

namespace ArchIntel.Tests;

public sealed class DocumentFilterTests
{
    [Theory]
    [InlineData("/repo/obj/Generated.cs")]
    [InlineData("/repo/bin/Debug/Example.cs")]
    [InlineData("/repo/.git/index")]
    [InlineData("/repo/.vs/solution.vsidx")]
    [InlineData("/repo/node_modules/lib/index.js")]
    [InlineData("/repo/src/Generated.g.cs")]
    [InlineData("/repo/src/Generated.g.i.cs")]
    [InlineData("/repo/src/Form.Designer.cs")]
    [InlineData("/repo/src/File.Generated.cs")]
    [InlineData("/repo/src/TemporaryGeneratedFile_foo.cs")]
    public void IsExcluded_ReturnsTrueForExcludedPaths(string path)
    {
        var filter = new DocumentFilter();

        var result = filter.IsExcluded(path);

        Assert.True(result);
    }

    [Fact]
    public void IsExcluded_ReturnsFalseForNormalSource()
    {
        var filter = new DocumentFilter();

        var result = filter.IsExcluded("/repo/src/App/Startup.cs");

        Assert.False(result);
    }
}
