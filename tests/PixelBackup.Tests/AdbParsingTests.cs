using PixelBackup.Core.Adb;
using Xunit;

namespace PixelBackup.Tests;

public class AdbParsingTests
{
    [Fact]
    public void ParseStatLines_ReadsSizeTimestampAndPath()
    {
        const string output = "1024|1700000000|/sdcard/DCIM/a.jpg\n2048|1700000100|/sdcard/DCIM/b b.jpg\n";
        var files = AdbClient.ParseStatLines(output);

        Assert.Equal(2, files.Count);
        Assert.Equal(1024, files[0].Size);
        Assert.Equal(1700000000, files[0].ModifiedUnix);
        Assert.Equal("/sdcard/DCIM/b b.jpg", files[1].Path);
    }

    [Fact]
    public void ParseStatLines_KeepsPipesInsideFileNames()
    {
        var files = AdbClient.ParseStatLines("10|1|/sdcard/Download/a|b.txt");
        Assert.Single(files);
        Assert.Equal("/sdcard/Download/a|b.txt", files[0].Path);
    }

    [Fact]
    public void ParseStatLines_IgnoresNoiseAndFractionalTimestamps()
    {
        const string output = "stat: '/sdcard/x': Permission denied\n512|1700000000.123456789|/sdcard/x.bin\n\n";
        var files = AdbClient.ParseStatLines(output);

        Assert.Single(files);
        Assert.Equal(512, files[0].Size);
        Assert.Equal(1700000000, files[0].ModifiedUnix);
    }

    [Fact]
    public void Quote_EscapesSingleQuotesForTheAndroidShell()
    {
        Assert.Equal("'/sdcard/Max'\\''s Bilder'", AdbClient.Quote("/sdcard/Max's Bilder"));
    }
}
