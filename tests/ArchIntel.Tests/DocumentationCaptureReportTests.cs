using ArchIntel.Reports;
using Xunit;

namespace ArchIntel.Tests;

public sealed class DocumentationCaptureReportTests
{
    [Fact]
    public void ExtractHeadings_ReturnsFirstHeadings()
    {
        var content = """
            # Title
            Intro text.
            ## Overview
            ### Details ###
            Not a heading
            #### Next Step
            """;

        var headings = DocumentationCaptureReport.ExtractHeadings(content, 3);

        Assert.Equal(new[] { "Title", "Overview", "Details" }, headings);
    }
}
