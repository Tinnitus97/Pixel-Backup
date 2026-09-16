using PixelBackup.Core.Model;
using PixelBackup.Core.Services;
using Xunit;

namespace PixelBackup.Tests;

public class ImportFileBuilderTests
{
    /// <summary>Der Abbruch-Token des laufenden Tests (xunit.v3).</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BuildAsync_CreatesVCardFromTheContactExport()
    {
        var directory = CreateTempDirectory();
        try
        {
            var dataDirectory = Path.Combine(directory, "data");
            Directory.CreateDirectory(dataDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(dataDirectory, "contacts-com.android.contacts-data-phones.txt"),
                "Row: 0 contact_id=1, display_name=Max Mustermann, data1=+49 170 1234567\n", Ct);

            var entries = new List<BackupEntry>
            {
                new()
                {
                    CategoryId = "contacts",
                    Type = BackupEntryType.ContentExport,
                    RemotePath = "content://com.android.contacts/data/phones",
                    RelativePath = "data/contacts-com.android.contacts-data-phones.txt"
                }
            };

            var created = await ImportFileBuilder.BuildAsync(directory, entries, ct: Ct);

            var entry = Assert.Single(created);
            Assert.Equal(BackupEntryType.ImportFile, entry.Type);
            Assert.Equal("data/kontakte.vcf", entry.RelativePath);
            Assert.StartsWith(ImportFileBuilder.ImportRemotePrefix, entry.RemotePath);

            var vcf = await File.ReadAllTextAsync(Path.Combine(dataDirectory, "kontakte.vcf"), Ct);
            Assert.Contains("FN:Max Mustermann", vcf);
            Assert.Contains("TEL;TYPE=CELL:+49 170 1234567", vcf);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_IgnoresExportsWithoutUsableRows()
    {
        var directory = CreateTempDirectory();
        try
        {
            var dataDirectory = Path.Combine(directory, "data");
            Directory.CreateDirectory(dataDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(dataDirectory, "sms-sms.txt"),
                "Error while accessing provider:sms\n", Ct);

            var entries = new List<BackupEntry>
            {
                new()
                {
                    CategoryId = "sms",
                    Type = BackupEntryType.ContentExport,
                    RemotePath = "content://sms",
                    RelativePath = "data/sms-sms.txt"
                }
            };

            Assert.Empty(await ImportFileBuilder.BuildAsync(directory, entries, ct: Ct));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_CreatesCalendarFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var dataDirectory = Path.Combine(directory, "data");
            Directory.CreateDirectory(dataDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(dataDirectory, "calendar-events.txt"),
                "Row: 0 _id=1, title=Zahnarzt, dtstart=1700000000000, dtend=1700003600000, allDay=0\n", Ct);

            var entries = new List<BackupEntry>
            {
                new()
                {
                    CategoryId = "calendar",
                    Type = BackupEntryType.ContentExport,
                    RemotePath = "content://com.android.calendar/events",
                    RelativePath = "data/calendar-events.txt"
                }
            };

            var created = await ImportFileBuilder.BuildAsync(directory, entries, ct: Ct);

            Assert.Single(created);
            Assert.True(File.Exists(Path.Combine(dataDirectory, "kalender.ics")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pixelbackup-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}
