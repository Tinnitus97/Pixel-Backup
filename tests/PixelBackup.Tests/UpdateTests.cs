using System.Runtime.InteropServices;
using PixelBackup.Core.Model;
using PixelBackup.Core.Services;
using Xunit;

namespace PixelBackup.Tests;

/// <summary>
/// Versionscheck und Updater: update.json lesen, Fassungen vergleichen, die
/// passende Datei wählen und den richtigen Einspielweg bestimmen.
/// </summary>
public class UpdateTests
{
    /// <summary>So sieht die Datei aus, die packaging/make-update-manifest.sh erzeugt.</summary>
    private const string SampleManifest = """
    {
      "schema": 1,
      "version": "0.9.1",
      "released": "2026-09-13",
      "notes": "https://github.com/Tinnitus97/Pixel-Backup/releases/tag/v0.9.1",
      "packages": [
        {
          "kind": "windows-exe",
          "arch": "x64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/PixelBackup-0.9.1-win-x64.exe",
          "sha256": "aa11",
          "size": 95765428
        },
        {
          "kind": "windows-exe",
          "arch": "arm64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/PixelBackup-0.9.1-win-arm64.exe",
          "sha256": "bb22",
          "size": 95765000
        },
        {
          "kind": "deb",
          "arch": "amd64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/pixel-backup_0.9.1_amd64.deb",
          "sha256": "cc33",
          "size": 33188892
        },
        {
          "kind": "rpm",
          "arch": "x86_64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/pixel-backup-0.9.1-1.x86_64.rpm",
          "sha256": "dd44",
          "size": 40702953
        },
        {
          "kind": "appimage",
          "arch": "x86_64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/PixelBackup-0.9.1-x86_64.AppImage",
          "sha256": "ee55",
          "size": 35744248
        },
        {
          "kind": "flatpak",
          "arch": "x86_64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/pixel-backup-0.9.1.flatpak",
          "sha256": "ff66",
          "size": 36000000
        },
        {
          "kind": "tarball",
          "arch": "x64",
          "url": "https://github.com/Tinnitus97/Pixel-Backup/releases/download/v0.9.1/pixel-backup-0.9.1-linux-x64.tar.gz",
          "sha256": "0077",
          "size": 40923992
        }
      ]
    }
    """;

    // ------------------------------------------------------------------ Lesen

    [Fact]
    public void Parse_ReadsEveryPackage()
    {
        var manifest = UpdateService.Parse(SampleManifest);

        Assert.True(manifest.Success);
        Assert.Equal("0.9.1", manifest.Version);
        Assert.Equal("2026-09-13", manifest.Released);
        Assert.Equal(7, manifest.Packages.Count);

        var deb = manifest.Packages.Single(p => p.Kind == InstallationKind.Deb);
        Assert.Equal("amd64", deb.Arch);
        Assert.Equal("cc33", deb.Sha256);
        Assert.Equal(33188892, deb.Size);
        Assert.Equal("pixel-backup_0.9.1_amd64.deb", deb.FileName);
    }

    [Fact]
    public void Parse_KnowsEveryKind()
    {
        var kinds = UpdateService.Parse(SampleManifest).Packages.Select(p => p.Kind).ToList();

        Assert.Contains(InstallationKind.WindowsExe, kinds);
        Assert.Contains(InstallationKind.Deb, kinds);
        Assert.Contains(InstallationKind.Rpm, kinds);
        Assert.Contains(InstallationKind.AppImage, kinds);
        Assert.Contains(InstallationKind.Flatpak, kinds);
        Assert.Contains(InstallationKind.Portable, kinds);
        Assert.DoesNotContain(InstallationKind.Unknown, kinds);
    }

    [Fact]
    public void Parse_RejectsFileWithoutVersion()
    {
        var manifest = UpdateService.Parse("""{ "schema": 1, "packages": [] }""");

        Assert.False(manifest.Success);
        Assert.NotNull(manifest.Error);
    }

    [Fact]
    public void Parse_SurvivesGarbage()
    {
        Assert.False(UpdateService.Parse("kein JSON").Success);
        Assert.False(UpdateService.Parse(string.Empty).Success);
    }

    [Fact]
    public void Parse_IgnoresUnusableEntries()
    {
        // Ohne Adresse und mit unbekannter Art ist ein Eintrag wertlos.
        var manifest = UpdateService.Parse("""
        {
          "version": "1.2.3",
          "packages": [
            { "kind": "deb", "arch": "amd64" },
            { "kind": "schneckenpost", "url": "https://example.invalid/x" },
            { "kind": "deb", "arch": "amd64", "url": "https://example.invalid/a.deb" }
          ]
        }
        """);

        Assert.True(manifest.Success);
        Assert.Single(manifest.Packages);
    }

    [Theory]
    [InlineData("windows-exe", InstallationKind.WindowsExe)]
    [InlineData("EXE", InstallationKind.WindowsExe)]
    [InlineData("deb", InstallationKind.Deb)]
    [InlineData("rpm", InstallationKind.Rpm)]
    [InlineData("AppImage", InstallationKind.AppImage)]
    [InlineData("flatpak", InstallationKind.Flatpak)]
    [InlineData("tarball", InstallationKind.Portable)]
    [InlineData("tar.gz", InstallationKind.Portable)]
    [InlineData("", InstallationKind.Unknown)]
    public void ParseKind_AcceptsTheUsualSpellings(string text, InstallationKind expected)
    {
        Assert.Equal(expected, UpdateService.ParseKind(text));
    }

    [Fact]
    public void KindKey_MatchesParseKind()
    {
        foreach (var kind in new[]
                 {
                     InstallationKind.WindowsExe, InstallationKind.Deb, InstallationKind.Rpm,
                     InstallationKind.AppImage, InstallationKind.Flatpak, InstallationKind.Portable
                 })
        {
            Assert.Equal(kind, UpdateService.ParseKind(UpdateService.KindKey(kind)));
        }
    }

    // ------------------------------------------------------------ Vergleichen

    [Theory]
    [InlineData("0.9.0", "0.9.1", true)]
    [InlineData("0.9.0", "0.10.0", true)]
    [InlineData("0.9.0", "1.0.0", true)]
    [InlineData("0.9.0", "v0.9.1", true)]
    [InlineData("0.9.0", "0.9.0", false)]
    [InlineData("0.9.1", "0.9.0", false)]
    [InlineData("1.0.0", "0.9.9", false)]
    [InlineData("0.9.0", "", false)]
    [InlineData("0.9.0", null, false)]
    [InlineData("0.9.0", "unbekannt", false)]
    public void IsNewer_ComparesNumbersNotText(string local, string? online, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewer(local, online));
    }

    [Fact]
    public void IsNewer_IgnoresSuffixes()
    {
        Assert.True(UpdateService.IsNewer("0.9.0", "0.9.1-beta"));
        Assert.False(UpdateService.IsNewer("0.9.0+abc", "0.9.0"));
    }

    // --------------------------------------------------------------- Auswählen

    [Theory]
    [InlineData(InstallationKind.WindowsExe, "x64", "win-x64.exe")]
    [InlineData(InstallationKind.WindowsExe, "arm64", "win-arm64.exe")]
    [InlineData(InstallationKind.Deb, "amd64", ".deb")]
    [InlineData(InstallationKind.Rpm, "x86_64", ".rpm")]
    [InlineData(InstallationKind.AppImage, "x86_64", ".AppImage")]
    [InlineData(InstallationKind.Flatpak, "x86_64", ".flatpak")]
    [InlineData(InstallationKind.Portable, "x64", ".tar.gz")]
    public void SelectPackage_PicksTheFileForThisInstallation(InstallationKind kind, string arch, string ending)
    {
        var manifest = UpdateService.Parse(SampleManifest);
        var package = UpdateService.SelectPackage(manifest, kind, arch);

        Assert.NotNull(package);
        Assert.Equal(kind, package.Kind);
        Assert.EndsWith(ending, package.Url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectPackage_WithoutMatchReturnsNothing()
    {
        var manifest = UpdateService.Parse(SampleManifest);

        // Für diese Architektur gibt es keine Datei, und ohne Angabe auch nicht.
        Assert.Null(UpdateService.SelectPackage(manifest, InstallationKind.Deb, "riscv64"));
        Assert.Null(UpdateService.SelectPackage(manifest, InstallationKind.Unknown, "x64"));
    }

    [Fact]
    public void SelectPackage_FallsBackToTheEntryWithoutArchitecture()
    {
        var manifest = UpdateService.Parse("""
        {
          "version": "9.9.9",
          "packages": [ { "kind": "flatpak", "url": "https://example.invalid/a.flatpak" } ]
        }
        """);

        var package = UpdateService.SelectPackage(manifest, InstallationKind.Flatpak, "aarch64");

        Assert.NotNull(package);
        Assert.Equal("a.flatpak", package.FileName);
    }

    // ------------------------------------------------------------- Einbauart

    [Fact]
    public void Detect_FlatpakAndAppImageAnnounceThemselves()
    {
        Assert.Equal(InstallationKind.Flatpak, InstallationInfo.Detect(
            "/app/lib/pixel-backup/PixelBackup", null, "io.github.tinnitus97.PixelBackup", false, false, true));

        Assert.Equal(InstallationKind.Flatpak, InstallationInfo.Detect(
            "/app/lib/pixel-backup/PixelBackup", null, null, hasFlatpakInfo: true, false, true));

        Assert.Equal(InstallationKind.AppImage, InstallationInfo.Detect(
            "/tmp/.mount_Pixel/usr/bin/PixelBackup", "/home/ich/PixelBackup.AppImage", null, false, false, true));
    }

    [Fact]
    public void Detect_WindowsIsAlwaysTheExe()
    {
        Assert.Equal(InstallationKind.WindowsExe, InstallationInfo.Detect(
            @"C:\Programme\PixelBackup\PixelBackup.exe", null, null, false, isWindows: true, isLinux: false));
    }

    [Fact]
    public void Detect_AsksThePackageManagerBelowUsr()
    {
        var kind = InstallationInfo.Detect(
            "/usr/lib/pixel-backup/PixelBackup", null, null, false, false, true,
            packageOwner: _ => InstallationKind.Deb);

        Assert.Equal(InstallationKind.Deb, kind);
    }

    [Fact]
    public void Detect_EverythingElseIsPortable()
    {
        var kind = InstallationInfo.Detect(
            "/home/ich/pixel-backup-0.9.0/PixelBackup", null, null, false, false, true,
            packageOwner: _ => InstallationKind.Unknown);

        Assert.Equal(InstallationKind.Portable, kind);

        // Ohne Pfad lässt sich nichts sagen.
        Assert.Equal(InstallationKind.Unknown, InstallationInfo.Detect(null, null, null, false, false, true));
    }

    [Theory]
    [InlineData(InstallationKind.Deb, Architecture.X64, "amd64")]
    [InlineData(InstallationKind.Deb, Architecture.Arm64, "arm64")]
    [InlineData(InstallationKind.Rpm, Architecture.X64, "x86_64")]
    [InlineData(InstallationKind.AppImage, Architecture.Arm64, "aarch64")]
    [InlineData(InstallationKind.WindowsExe, Architecture.X64, "x64")]
    [InlineData(InstallationKind.WindowsExe, Architecture.Arm64, "arm64")]
    public void ArchOf_UsesTheNamingOfEachFormat(InstallationKind kind, Architecture architecture, string expected)
    {
        Assert.Equal(expected, InstallationInfo.ArchOf(kind, architecture));
    }

    [Fact]
    public void CurrentVersion_IsAReadableNumber()
    {
        Assert.True(Version.TryParse(InstallationInfo.CurrentVersion, out var version));
        Assert.True(version!.Major >= 0);
    }

    // ------------------------------------------------------------- Einspielen

    [Fact]
    public void BuildPackageCommand_UsesThePackageManager()
    {
        var deb = UpdateInstaller.BuildPackageCommand(
            InstallationKind.Deb, "/tmp/pixel-backup.deb", LinuxPackageManager.Apt);

        Assert.NotNull(deb);
        Assert.Equal("pkexec", deb.Value.FileName);
        Assert.Contains("apt-get", deb.Value.Arguments);
        Assert.Contains("/tmp/pixel-backup.deb", deb.Value.Arguments);

        var rpm = UpdateInstaller.BuildPackageCommand(
            InstallationKind.Rpm, "/tmp/pixel-backup.rpm", LinuxPackageManager.Dnf);

        Assert.NotNull(rpm);
        Assert.Contains("dnf", rpm.Value.Arguments);

        var zypper = UpdateInstaller.BuildPackageCommand(
            InstallationKind.Rpm, "/tmp/pixel-backup.rpm", LinuxPackageManager.Zypper);

        Assert.NotNull(zypper);
        Assert.Contains("zypper", zypper.Value.Arguments);
    }

    [Fact]
    public void BuildPackageCommand_FlatpakNeedsNoAdministrator()
    {
        var command = UpdateInstaller.BuildPackageCommand(
            InstallationKind.Flatpak, "/tmp/pixel-backup.flatpak", LinuxPackageManager.Apt);

        Assert.NotNull(command);
        Assert.Equal("flatpak", command.Value.FileName);
        Assert.Contains("--user", command.Value.Arguments);
        Assert.Contains("--bundle", command.Value.Arguments);
    }

    [Fact]
    public void BuildPackageCommand_InsideTheSandboxGoesThroughTheHost()
    {
        var command = UpdateInstaller.BuildPackageCommand(
            InstallationKind.Flatpak, "/tmp/a.flatpak", LinuxPackageManager.Apt, inSandbox: true);

        Assert.NotNull(command);
        Assert.Equal("flatpak-spawn", command.Value.FileName);
        Assert.Equal("--host", command.Value.Arguments[0]);
        Assert.Contains("flatpak", command.Value.Arguments);
    }

    [Fact]
    public void BuildPackageCommand_HasNoPathForSwappedFiles()
    {
        // EXE, AppImage und portable Fassung werden ausgetauscht, nicht installiert.
        Assert.Null(UpdateInstaller.BuildPackageCommand(
            InstallationKind.WindowsExe, "x.exe", LinuxPackageManager.Unknown));
        Assert.Null(UpdateInstaller.BuildPackageCommand(
            InstallationKind.AppImage, "x.AppImage", LinuxPackageManager.Unknown));
    }

    [Fact]
    public void UnixScript_WaitsForTheProcessAndRestarts()
    {
        var script = UpdateInstaller.BuildUnixScript("/tmp/neu/PixelBackup", "/home/ich/PixelBackup", 4711);

        Assert.StartsWith("#!/bin/sh", script);
        Assert.Contains("kill -0 4711", script);
        Assert.Contains("/tmp/neu/PixelBackup", script);
        Assert.Contains("/home/ich/PixelBackup", script);
        Assert.Contains("chmod +x", script);
    }

    [Fact]
    public void WindowsScript_WaitsForTheProcessAndRestarts()
    {
        var script = UpdateInstaller.BuildWindowsScript(@"C:\Temp\neu.exe", @"C:\Tools\PixelBackup.exe", 4711);

        Assert.Contains("PID eq 4711", script);
        Assert.Contains(@"C:\Temp\neu.exe", script);
        Assert.Contains(@"C:\Tools\PixelBackup.exe", script);
        Assert.Contains("move /y", script);
    }

    [Theory]
    [InlineData("pixel-backup-0.9.0/PixelBackup", true)]
    [InlineData("PixelBackup", true)]
    [InlineData("PixelBackup.exe", true)]
    [InlineData("pixel-backup-0.9.0/install.sh", false)]
    [InlineData("../../../etc/passwd", false)]
    [InlineData("a/../PixelBackup", false)]
    [InlineData("pixel-backup-0.9.0/icons/pixel-backup-48.png", false)]
    public void IsProgramEntry_TakesOnlyTheProgramItself(string entry, bool expected)
    {
        Assert.Equal(expected, UpdateInstaller.IsProgramEntry(entry));
    }

    [Fact]
    public void FileName_ComesFromTheAddress()
    {
        var package = new UpdatePackage { Kind = InstallationKind.Deb, Url = "https://example.invalid/a/b/x.deb?raw=1" };
        Assert.Equal("x.deb", package.FileName);

        Assert.Equal("pixel-backup-update", new UpdatePackage().FileName);
    }

    // --------------------------------------------------------------- Ergebnis

    [Fact]
    public void CheckResult_SaysWhatIsGoingOn()
    {
        var upToDate = new UpdateCheckResult { LocalVersion = "0.9.0", UpdateAvailable = false };
        Assert.Contains("0.9.0", upToDate.Describe());
        Assert.False(upToDate.CanInstall);

        var available = new UpdateCheckResult
        {
            LocalVersion = "0.9.0",
            OnlineVersion = "0.9.1",
            UpdateAvailable = true,
            Package = new UpdatePackage { Kind = InstallationKind.Deb, Url = "https://example.invalid/x.deb" }
        };
        Assert.Contains("0.9.1", available.Describe());
        Assert.True(available.CanInstall);

        var noFile = new UpdateCheckResult { LocalVersion = "0.9.0", OnlineVersion = "0.9.1", UpdateAvailable = true };
        Assert.False(noFile.CanInstall);

        var failed = new UpdateCheckResult { LocalVersion = "0.9.0", Error = "kein Netz" };
        Assert.Contains("kein Netz", failed.Describe());
    }
}
