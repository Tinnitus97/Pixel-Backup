using System.Globalization;
using PixelBackup.Core.Util;
using Xunit;

namespace PixelBackup.Tests;

public class HumanizeTests
{
    private static void UseGermanCulture() =>
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

    [Fact]
    public void Bytes_UsesFittingUnits()
    {
        UseGermanCulture();
        Assert.Equal("512 B", Humanize.Bytes(512));
        Assert.Equal("1,00 KB", Humanize.Bytes(1024));
        Assert.Equal("1,50 MB", Humanize.Bytes(1024 * 1024 * 3 / 2));
        Assert.Equal("–", Humanize.Bytes(-1));
    }

    [Fact]
    public void Duration_SwitchesBetweenSecondsMinutesAndHours()
    {
        UseGermanCulture();
        Assert.Equal("45 s", Humanize.Duration(TimeSpan.FromSeconds(45)));
        Assert.Equal("2:05 min", Humanize.Duration(TimeSpan.FromSeconds(125)));
        Assert.Equal("1:01:05 h", Humanize.Duration(TimeSpan.FromSeconds(3665)));
    }

    [Fact]
    public void Speed_ReturnsDashWithoutThroughput()
    {
        Assert.Equal("–", Humanize.Speed(0));
        Assert.Contains("/s", Humanize.Speed(1024 * 1024));
    }

    [Fact]
    public void Count_UsesSingularAndPlural()
    {
        UseGermanCulture();
        Assert.Equal("1 Datei", Humanize.Count(1, "Datei", "Dateien"));
        Assert.Equal("2 Dateien", Humanize.Count(2, "Datei", "Dateien"));
    }
}
