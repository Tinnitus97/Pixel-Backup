using PixelBackup.Core.Util;
using Xunit;

namespace PixelBackup.Tests;

public class PathMapperTests
{
    [Fact]
    public void ToRelativeBackupPath_KeepsStructureBelowFilesFolder()
    {
        var relative = PathMapper.ToRelativeBackupPath("/sdcard/DCIM/Camera/IMG_0001.jpg");
        Assert.Equal("files/sdcard/DCIM/Camera/IMG_0001.jpg", relative);
    }

    [Fact]
    public void ToRelativeBackupPath_ReplacesCharactersWindowsCannotStore()
    {
        var relative = PathMapper.ToRelativeBackupPath("/sdcard/Download/Rechnung:2024?.pdf");
        Assert.Equal("files/sdcard/Download/Rechnung_2024_.pdf", relative);
    }

    [Fact]
    public void ToLocalPath_CombinesWithSetDirectory()
    {
        var local = PathMapper.ToLocalPath(Path.Combine("C:", "Sicherung"), "files/sdcard/a.txt");
        Assert.Equal(Path.Combine("C:", "Sicherung", "files", "sdcard", "a.txt"), local);
    }

    [Theory]
    [InlineData("/sdcard/DCIM/a.jpg", "/sdcard/DCIM", true)]
    [InlineData("/sdcard/DCIM/Camera/a.jpg", "/sdcard/DCIM/", true)]
    [InlineData("/sdcard/DCIMX/a.jpg", "/sdcard/DCIM", false)]
    [InlineData("/sdcard/DCIM", "/sdcard/DCIM", false)]
    public void IsInsideRemoteDirectory_ComparesWholeSegments(string path, string directory, bool expected)
    {
        Assert.Equal(expected, PathMapper.IsInsideRemoteDirectory(path, directory));
    }

    [Fact]
    public void AppendSuffix_InsertsBeforeExtension()
    {
        Assert.Equal("/sdcard/DCIM/a_neu.jpg", PathMapper.AppendSuffix("/sdcard/DCIM/a.jpg", "_neu"));
        Assert.Equal("/sdcard/DCIM/ohneEndung_neu", PathMapper.AppendSuffix("/sdcard/DCIM/ohneEndung", "_neu"));
    }

    [Fact]
    public void RemoteHelpers_SplitPathsCorrectly()
    {
        Assert.Equal("/sdcard/DCIM", PathMapper.RemoteDirectory("/sdcard/DCIM/a.jpg"));
        Assert.Equal("a.jpg", PathMapper.RemoteFileName("/sdcard/DCIM/a.jpg"));
        Assert.Equal("jpg", PathMapper.RemoteExtension("/sdcard/DCIM/a.JPG"));
        Assert.Equal(string.Empty, PathMapper.RemoteExtension("/sdcard/DCIM/a"));
    }
}
