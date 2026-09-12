using System.Security.Cryptography;
using System.Text;
using PixelBackup.Core.Services;
using Xunit;

namespace PixelBackup.Tests;

public class ArchiveServiceTests
{
    [Fact]
    public async Task EncryptAndDecrypt_RestoresTheOriginalContent()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "daten.bin");
            var content = RandomNumberGenerator.GetBytes(3 * 1024 * 1024 + 12345);
            await File.WriteAllBytesAsync(source, content);

            var encrypted = Path.Combine(directory, "daten.pbenc");
            var decrypted = Path.Combine(directory, "daten-zurueck.bin");

            await ArchiveService.EncryptFileAsync(source, encrypted, "sehr-geheim");
            await ArchiveService.DecryptFileAsync(encrypted, decrypted, "sehr-geheim");

            Assert.Equal(content, await File.ReadAllBytesAsync(decrypted));
            Assert.NotEqual(content.LongLength, new FileInfo(encrypted).Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Decrypt_FailsWithWrongPassword()
    {
        var directory = CreateTempDirectory();
        try
        {
            var source = Path.Combine(directory, "klein.txt");
            await File.WriteAllTextAsync(source, "Geheime Notizen", Encoding.UTF8);

            var encrypted = Path.Combine(directory, "klein.pbenc");
            await ArchiveService.EncryptFileAsync(source, encrypted, "richtig");

            await Assert.ThrowsAsync<CryptographicException>(
                () => ArchiveService.DecryptFileAsync(encrypted, Path.Combine(directory, "raus.txt"), "falsch"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateArchive_PacksTheWholeSetIntoAZipFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var setDirectory = Path.Combine(directory, "2024-01-01_10-00-00");
            Directory.CreateDirectory(Path.Combine(setDirectory, "files", "sdcard"));
            await File.WriteAllTextAsync(Path.Combine(setDirectory, "files", "sdcard", "a.txt"), "Inhalt");
            await File.WriteAllTextAsync(Path.Combine(setDirectory, "manifest.json"), "{}");

            var archive = await ArchiveService.CreateArchiveAsync(setDirectory, password: null);

            Assert.True(File.Exists(archive));
            Assert.EndsWith(".zip", archive);

            using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
            Assert.Contains(zip.Entries, e => e.FullName == "files/sdcard/a.txt");
            Assert.Contains(zip.Entries, e => e.FullName == "manifest.json");
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
