using PixelBackup.Core.Model;
using Xunit;

namespace PixelBackup.Tests;

public class ManifestTests
{
    [Fact]
    public void Manifest_SurvivesJsonRoundTrip()
    {
        var manifest = new BackupManifest
        {
            Device = new DeviceInfo { Serial = "ABC123", Model = "Pixel 8", Manufacturer = "Google" },
            Categories = { "photos", "apps" },
            Entries =
            {
                new BackupEntry
                {
                    CategoryId = "photos",
                    Type = BackupEntryType.File,
                    RemotePath = "/sdcard/DCIM/a.jpg",
                    RelativePath = "files/sdcard/DCIM/a.jpg",
                    Size = 2048,
                    ModifiedUnix = 1700000000,
                    Sha256 = "abcdef"
                },
                new BackupEntry
                {
                    CategoryId = "apps",
                    Type = BackupEntryType.Apk,
                    RemotePath = "/data/app/base.apk",
                    RelativePath = "apps/com.example/base.apk",
                    Size = 1024,
                    PackageName = "com.example"
                }
            }
        };

        var restored = BackupManifest.FromJson(manifest.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(2, restored!.Entries.Count);
        Assert.Equal(3072, restored.TotalBytes);
        Assert.Equal(BackupEntryType.Apk, restored.Entries[1].Type);
        Assert.Equal("com.example", restored.Entries[1].PackageName);
        Assert.Equal("Google Pixel 8", restored.Device.DisplayName);
    }

    [Fact]
    public void Manifest_SavesAndLoadsFromDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pixelbackup-test-" + Guid.NewGuid().ToString("n"));
        try
        {
            var manifest = new BackupManifest { Device = new DeviceInfo { Serial = "XYZ" } };
            manifest.Save(directory);

            var loaded = BackupManifest.TryLoad(directory);
            Assert.NotNull(loaded);
            Assert.Equal("XYZ", loaded!.Device.Serial);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void TryLoad_ReturnsNullWithoutManifest()
    {
        Assert.Null(BackupManifest.TryLoad(Path.Combine(Path.GetTempPath(), "gibt-es-nicht-" + Guid.NewGuid())));
    }

    [Fact]
    public void BackupSet_MapsEntriesToLocalFiles()
    {
        var manifest = new BackupManifest();
        var entry = new BackupEntry { RelativePath = "files/sdcard/DCIM/a.jpg" };
        manifest.Entries.Add(entry);

        var root = Path.Combine(Path.GetTempPath(), "satz");
        var set = new BackupSet(root, manifest);

        Assert.Equal(Path.Combine(root, "files", "sdcard", "DCIM", "a.jpg"), set.LocalPathOf(entry));
    }
}
