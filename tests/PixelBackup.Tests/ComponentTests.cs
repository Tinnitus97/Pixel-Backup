using PixelBackup.Core.Model;
using PixelBackup.Core.Services;
using Xunit;

namespace PixelBackup.Tests;

public class ComponentTests
{
    private const string ToolsManifest = """
        <?xml version="1.0" ?>
        <sdk:sdk-repository xmlns:sdk="http://schemas.android.com/sdk/android/repo/repository2/03">
          <remotePackage path="build-tools;35.0.0">
            <revision><major>35</major><minor>0</minor><micro>0</micro></revision>
            <display-name>Android SDK Build-Tools</display-name>
            <archives>
              <archive>
                <complete>
                  <size>1</size>
                  <checksum type="sha1">1111111111111111111111111111111111111111</checksum>
                  <url>build-tools_r35-windows.zip</url>
                </complete>
                <host-os>windows</host-os>
              </archive>
            </archives>
          </remotePackage>
          <remotePackage path="platform-tools">
            <revision><major>36</major><minor>0</minor><micro>1</micro></revision>
            <display-name>Android SDK Platform-Tools</display-name>
            <archives>
              <archive>
                <complete>
                  <size>7000000</size>
                  <checksum type="sha1">abcdefabcdefabcdefabcdefabcdefabcdefabcd</checksum>
                  <url>platform-tools_r36.0.1-linux.zip</url>
                </complete>
                <host-os>linux</host-os>
              </archive>
              <archive>
                <complete>
                  <size>6500000</size>
                  <checksum type="sha1">1234567890123456789012345678901234567890</checksum>
                  <url>platform-tools_r36.0.1-windows.zip</url>
                </complete>
                <host-os>windows</host-os>
              </archive>
            </archives>
          </remotePackage>
        </sdk:sdk-repository>
        """;

    [Fact]
    public void ParsePackage_PicksThePackageAndTheMatchingHost()
    {
        var package = SdkRepositoryClient.ParsePackage(ToolsManifest, "platform-tools", "windows");

        Assert.NotNull(package);
        Assert.Equal(new Version(36, 0, 1), package!.Revision);
        Assert.Equal("Android SDK Platform-Tools", package.DisplayName);
        Assert.Equal("https://dl.google.com/android/repository/platform-tools_r36.0.1-windows.zip", package.DownloadUrl);
        Assert.Equal(6500000, package.Size);
        Assert.Equal("1234567890123456789012345678901234567890", package.Sha1);
    }

    [Fact]
    public void ParsePackage_PicksTheLinuxArchiveOnLinux()
    {
        var package = SdkRepositoryClient.ParsePackage(ToolsManifest, "platform-tools", "linux");
        Assert.EndsWith("platform-tools_r36.0.1-linux.zip", package!.DownloadUrl);
    }

    [Fact]
    public void ParsePackage_ReturnsNullForUnknownPackages()
    {
        Assert.Null(SdkRepositoryClient.ParsePackage(ToolsManifest, "extras;google;usb_driver", "windows"));
    }

    [Theory]
    [InlineData("Android Debug Bridge version 1.0.41\nVersion 35.0.2-12147458\n", 35, 0, 2)]
    [InlineData("Android Debug Bridge version 1.0.41\nVersion 36.0.1\nInstalled as /usr/bin/adb\n", 36, 0, 1)]
    public void ParseVersion_ReadsTheToolsVersion(string output, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), PlatformToolsInstaller.ParseVersion(output));
    }

    [Fact]
    public void ParseVersion_ReturnsNullWithoutAVersionLine()
    {
        Assert.Null(PlatformToolsInstaller.ParseVersion("Android Debug Bridge version 1.0.41"));
    }

    [Fact]
    public void IsManaged_RecognisesTheOwnInstallation()
    {
        Assert.True(PlatformToolsInstaller.IsManaged(PlatformToolsInstaller.ManagedAdbPath));
        Assert.False(PlatformToolsInstaller.IsManaged(Path.Combine("C:", "platform-tools", "adb.exe")));
    }

    [Fact]
    public void ParseReport_ReadsDevicesAndDrivers()
    {
        const string json = """
            {"StoreHasGoogle":true,
             "Devices":[{"Name":"Pixel 8 Pro","Status":"OK","ConfigManagerErrorCode":0},
                        {"Name":"Android ADB Interface","Status":"Error","ConfigManagerErrorCode":28}],
             "Drivers":[{"InfName":"android_winusb.inf","DriverVersion":"13.0.0.0","DriverProviderName":"Google, Inc."}]}
            """;

        var report = UsbDriverService.ParseReport(json);

        Assert.NotNull(report);
        Assert.True(report!.StoreHasGoogle);
        Assert.Equal(2, report.Devices.Count);
        Assert.False(report.Devices[0].HasProblem);
        Assert.True(report.Devices[1].HasProblem);
        Assert.Equal("13.0.0.0", report.Drivers[0].DriverVersion);
    }

    [Fact]
    public void ParseReport_HandlesEmptyAndBrokenInput()
    {
        var empty = UsbDriverService.ParseReport("""{"StoreHasGoogle":false,"Devices":[],"Drivers":[]}""");
        Assert.NotNull(empty);
        Assert.Empty(empty!.Devices);

        Assert.Null(UsbDriverService.ParseReport("kein JSON"));
    }

    [Fact]
    public void ComponentStatus_DescribesItself()
    {
        var status = new ComponentStatus
        {
            Name = "adb",
            State = ComponentState.UpdateAvailable,
            InstalledVersion = "35.0.2",
            LatestVersion = "36.0.1"
        };

        Assert.True(status.CanUpdate);
        Assert.False(status.CanInstall);
        Assert.False(status.IsHealthy);
        Assert.Equal("35.0.2 → 36.0.1", status.VersionText);

        var ok = new ComponentStatus { Name = "adb", State = ComponentState.UpToDate, InstalledVersion = "36.0.1" };
        Assert.True(ok.IsHealthy);
        Assert.Equal("36.0.1", ok.VersionText);
    }
}
