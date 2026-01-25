using ArchIntel.Logging;
using Xunit;

namespace ArchIntel.Tests;

public sealed class SafeLogTests
{
    [Fact]
    public void SanitizeValue_RemovesLineBreaks()
    {
        var value = "first line\nsecond line\r\nthird line";

        var sanitized = SafeLog.SanitizeValue(value);

        Assert.DoesNotContain("\n", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", sanitized, StringComparison.Ordinal);
        Assert.Contains("first line", sanitized, StringComparison.Ordinal);
        Assert.Contains("second line", sanitized, StringComparison.Ordinal);
        Assert.Contains("third line", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeValue_TruncatesLongValues()
    {
        var value = new string('a', 300);

        var sanitized = SafeLog.SanitizeValue(value);

        Assert.Equal(259, sanitized.Length);
        Assert.EndsWith("...", sanitized, StringComparison.Ordinal);
    }
}
