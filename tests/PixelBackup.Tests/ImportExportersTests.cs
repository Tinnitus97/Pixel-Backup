using System.Globalization;
using System.Xml.Linq;
using PixelBackup.Core.Util;
using Xunit;

namespace PixelBackup.Tests;

public class ImportExportersTests
{
    private static List<Dictionary<string, string>> Rows(string output) => ContentRowParser.Parse(output);

    [Fact]
    public void BuildVCards_MergesNumbersAndMailsOfTheSameContact()
    {
        var phones = Rows("""
                          Row: 0 contact_id=7, display_name=Max Mustermann, data1=+49 170 1234567
                          Row: 1 contact_id=7, display_name=Max Mustermann, data1=+49 30 987654
                          Row: 2 contact_id=9, display_name=Erika Beispiel, data1=+49 171 5555
                          """);
        var mails = Rows("Row: 0 contact_id=7, display_name=Max Mustermann, data1=max@example.org");

        var vcf = ImportExporters.BuildVCards(phones, mails);

        Assert.Equal(2, vcf.Split("BEGIN:VCARD").Length - 1);
        Assert.Contains("FN:Max Mustermann", vcf);
        Assert.Contains("TEL;TYPE=CELL:+49 170 1234567", vcf);
        Assert.Contains("TEL;TYPE=CELL:+49 30 987654", vcf);
        Assert.Contains("EMAIL;TYPE=INTERNET:max@example.org", vcf);
        Assert.Contains("FN:Erika Beispiel", vcf);
        Assert.EndsWith("END:VCARD\r\n", vcf);
    }

    [Fact]
    public void BuildVCards_EscapesSpecialCharacters()
    {
        var phones = Rows("Row: 0 contact_id=1, display_name=Muster; GmbH, data1=+4930123");
        var vcf = ImportExporters.BuildVCards(phones);

        Assert.Contains(@"FN:Muster\; GmbH", vcf);
    }

    [Fact]
    public void BuildSmsXml_ProducesTheFormatOfSmsBackupAndRestore()
    {
        var rows = Rows("""
                        Row: 0 _id=1, address=+4917012345, date=1700000000000, type=1, body=Hallo Welt, read=1
                        Row: 1 _id=2, address=+4917012345, date=1700000100000, type=2, body=Bis gleich, read=1
                        """);

        var xml = ImportExporters.BuildSmsXml(rows);
        var document = XDocument.Parse(xml);

        Assert.Equal("smses", document.Root!.Name.LocalName);
        Assert.Equal("2", document.Root.Attribute("count")!.Value);

        var first = document.Root.Elements("sms").First();
        Assert.Equal("+4917012345", first.Attribute("address")!.Value);
        Assert.Equal("Hallo Welt", first.Attribute("body")!.Value);
        Assert.Equal("1700000000000", first.Attribute("date")!.Value);
        Assert.Equal("1", first.Attribute("type")!.Value);
    }

    [Fact]
    public void BuildCallsXml_KeepsNumberDurationAndType()
    {
        var rows = Rows("Row: 0 _id=1, number=+4930123, duration=42, date=1700000000000, type=2, name=Erika");
        var document = XDocument.Parse(ImportExporters.BuildCallsXml(rows));

        var call = document.Root!.Elements("call").Single();
        Assert.Equal("+4930123", call.Attribute("number")!.Value);
        Assert.Equal("42", call.Attribute("duration")!.Value);
        Assert.Equal("2", call.Attribute("type")!.Value);
        Assert.Equal("Erika", call.Attribute("contact_name")!.Value);
    }

    [Fact]
    public void BuildIcs_WritesEventsWithCorrectTimestamps()
    {
        const long start = 1700000000000;
        var rows = Rows($"Row: 0 _id=5, title=Zahnarzt, dtstart={start}, dtend={start + 3600000}, eventLocation=Berlin, allDay=0");

        var ics = ImportExporters.BuildIcs(rows);
        var expected = DateTimeOffset.FromUnixTimeMilliseconds(start).UtcDateTime
            .ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        Assert.StartsWith("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("SUMMARY:Zahnarzt", ics);
        Assert.Contains("LOCATION:Berlin", ics);
        Assert.Contains("DTSTART:" + expected, ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void BuildIcs_UsesDateOnlyForAllDayEvents()
    {
        var rows = Rows("Row: 0 _id=6, title=Urlaub, dtstart=1700000000000, allDay=1");
        var ics = ImportExporters.BuildIcs(rows);

        Assert.Contains("DTSTART;VALUE=DATE:", ics);
        Assert.DoesNotContain("DTEND", ics);
    }

    [Fact]
    public void Exporters_HandleEmptyInputGracefully()
    {
        var empty = new List<Dictionary<string, string>>();

        Assert.Equal(string.Empty, ImportExporters.BuildVCards(empty));
        Assert.Contains("count=\"0\"", ImportExporters.BuildSmsXml(empty));
        Assert.Contains("count=\"0\"", ImportExporters.BuildCallsXml(empty));
        Assert.Contains("END:VCALENDAR", ImportExporters.BuildIcs(empty));
    }
}
