using PixelBackup.Core.Adb;
using PixelBackup.Core.Services;
using Xunit;

namespace PixelBackup.Tests;

public class LinuxSupportTests
{
    [Fact]
    public void BuildRules_ContainsOneLinePerVendor()
    {
        var rules = LinuxDeviceAccessService.BuildRules();
        var ruleLines = rules
            .Split('\n')
            .Where(l => l.StartsWith("SUBSYSTEM==", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(LinuxDeviceAccessService.Vendors.Count, ruleLines.Count);
        Assert.All(ruleLines, line =>
        {
            Assert.Contains("ATTR{idVendor}==", line);
            Assert.Contains("MODE=\"0660\"", line);
            Assert.Contains("GROUP=\"plugdev\"", line);
            Assert.Contains("TAG+=\"uaccess\"", line);
        });
    }

    [Fact]
    public void BuildRules_CoversTheCommonManufacturers()
    {
        var rules = LinuxDeviceAccessService.BuildRules();

        foreach (var vendorId in new[] { "18d1", "04e8", "2717", "2a70", "12d1", "0fce", "22b8" })
        {
            Assert.Contains($"\"{vendorId}\"", rules);
        }
    }

    [Fact]
    public void BuildInstallScript_PlacesTheRulesAndReloadsThem()
    {
        var script = LinuxDeviceAccessService.BuildInstallScript("/tmp/quelle.rules", "tino");

        Assert.StartsWith("#!/bin/sh", script);
        Assert.Contains("install -D -m 0644 -o root -g root '/tmp/quelle.rules' '/etc/udev/rules.d/51-android.rules'", script);
        Assert.Contains("usermod -aG plugdev 'tino'", script);
        Assert.Contains("udevadm control --reload-rules", script);
        Assert.Contains("udevadm trigger", script);
    }

    [Theory]
    [InlineData("ID=ubuntu\nPRETTY_NAME=\"Ubuntu 24.04.4 LTS\"\nID_LIKE=debian\n", "ID", "ubuntu")]
    [InlineData("ID=ubuntu\nPRETTY_NAME=\"Ubuntu 24.04.4 LTS\"\n", "PRETTY_NAME", "Ubuntu 24.04.4 LTS")]
    [InlineData("NAME=\"Arch Linux\"\nID=arch\n", "ID", "arch")]
    [InlineData("ID=fedora\n", "ID_LIKE", "")]
    public void ParseOsRelease_ReadsValuesWithAndWithoutQuotes(string content, string key, string expected)
    {
        Assert.Equal(expected, LinuxEnvironment.ParseOsRelease(content, key));
    }

    [Fact]
    public void ParseDeviceList_RecognisesMissingPermissions()
    {
        const string output = """
                              List of devices attached
                              2B0CD9G3K1004Q	no permissions (user in plugdev group; are your udev rules wrong?); see [http://developer.android.com/tools/device.html]
                              192.168.1.50:5555	device product:raven model:Pixel_8_Pro device:raven transport_id:3
                              """;

        var devices = AdbClient.ParseDeviceList(output);

        Assert.Equal(2, devices.Count);
        Assert.Equal(AdbDeviceState.NoPermissions, devices[0].State);
        Assert.False(devices[0].IsReady);
        Assert.Equal(AdbDeviceState.Device, devices[1].State);
        Assert.True(devices[1].IsReady);
        Assert.Equal("Pixel_8_Pro", devices[1].Model);
        Assert.True(devices[1].IsWireless);
    }

    [Fact]
    public void ParseDeviceList_HandlesTheUsualStates()
    {
        const string output = """
                              List of devices attached
                              ABC123	unauthorized
                              DEF456	offline
                              GHI789	device
                              * daemon started successfully *
                              """;

        var devices = AdbClient.ParseDeviceList(output);

        Assert.Equal(3, devices.Count);
        Assert.Equal(AdbDeviceState.Unauthorized, devices[0].State);
        Assert.Equal(AdbDeviceState.Offline, devices[1].State);
        Assert.Equal(AdbDeviceState.Device, devices[2].State);
    }

    [Fact]
    public void ParseDeviceList_IgnoresNoiseAndEmptyOutput()
    {
        Assert.Empty(AdbClient.ParseDeviceList("List of devices attached\n\n"));
        Assert.Empty(AdbClient.ParseDeviceList(string.Empty));
    }

    [Fact]
    public void RulesPath_PointsIntoTheSystemRulesDirectory()
    {
        Assert.Equal("/etc/udev/rules.d/51-android.rules", LinuxDeviceAccessService.RulesPath);
        Assert.Equal("51-android.rules", LinuxDeviceAccessService.RulesFileName);
    }
}
