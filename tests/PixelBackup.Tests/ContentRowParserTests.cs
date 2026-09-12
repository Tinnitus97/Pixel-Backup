using PixelBackup.Core.Util;
using Xunit;

namespace PixelBackup.Tests;

public class ContentRowParserTests
{
    [Fact]
    public void Parse_ReadsRowsFromContentQueryOutput()
    {
        const string output = """
                              Row: 0 _id=1, display_name=Max Mustermann, data1=+49 170 1234567
                              Row: 1 _id=2, display_name=Erika, data1=+49 171 7654321
                              """;

        var rows = ContentRowParser.Parse(output);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Max Mustermann", rows[0]["display_name"]);
        Assert.Equal("+49 171 7654321", rows[1]["data1"]);
    }

    [Fact]
    public void Parse_IgnoresLinesThatAreNotRows()
    {
        var rows = ContentRowParser.Parse("Error while accessing provider:sms\nNo result found.\n");
        Assert.Empty(rows);
    }

    [Fact]
    public void ToCsv_WritesHeaderAndEscapesSeparators()
    {
        var rows = ContentRowParser.Parse("Row: 0 name=Muster; GmbH, number=123");
        var csv = ContentRowParser.ToCsv(rows);

        Assert.Contains("name;number", csv);
        Assert.Contains("\"Muster; GmbH\"", csv);
    }

    [Fact]
    public void ToCsv_ReturnsEmptyTextWithoutRows()
    {
        Assert.Equal(string.Empty, ContentRowParser.ToCsv(new List<Dictionary<string, string>>()));
    }
}
