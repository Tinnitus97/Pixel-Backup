using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PixelBackup.Core.Services;

/// <summary>
/// Packt einen Sicherungssatz in ein ZIP-Archiv und verschlüsselt es auf Wunsch
/// mit AES-256-GCM (Schlüssel aus dem Kennwort per PBKDF2).
/// </summary>
public static class ArchiveService
{
    private const int ChunkSize = 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Pbkdf2Iterations = 210_000;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PBK1");

    public const string EncryptedExtension = ".pbenc";

    /// <summary>Erstellt das Archiv und gibt dessen Pfad zurück.</summary>
    public static async Task<string> CreateArchiveAsync(
        string setDirectory,
        string? password,
        CancellationToken ct = default)
    {
        var parent = Path.GetDirectoryName(setDirectory.TrimEnd(Path.DirectorySeparatorChar))
                     ?? Directory.GetCurrentDirectory();
        var name = Path.GetFileName(setDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var zipPath = Path.Combine(parent, name + ".zip");

        await CreateZipAsync(setDirectory, zipPath, ct).ConfigureAwait(false);

        if (string.IsNullOrEmpty(password))
        {
            return zipPath;
        }

        var encryptedPath = zipPath + EncryptedExtension;
        await EncryptFileAsync(zipPath, encryptedPath, password, ct).ConfigureAwait(false);
        File.Delete(zipPath);
        return encryptedPath;
    }

    public static async Task CreateZipAsync(string sourceDirectory, string zipPath, CancellationToken ct = default)
    {
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        await using var zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var root = Path.GetFullPath(sourceDirectory);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);

            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var target = entry.Open();
            await source.CopyToAsync(target, 81920, ct).ConfigureAwait(false);
        }
    }

    public static async Task EncryptFileAsync(
        string sourcePath,
        string targetPath,
        string password,
        CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        await output.WriteAsync(Magic, ct).ConfigureAwait(false);
        await output.WriteAsync(salt, ct).ConfigureAwait(false);

        using var aes = new AesGcm(key, TagSize);
        var plain = new byte[ChunkSize];
        var cipher = new byte[ChunkSize];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var header = new byte[4];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(plain.AsMemory(0, ChunkSize), ct).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            RandomNumberGenerator.Fill(nonce);
            aes.Encrypt(nonce, plain.AsSpan(0, read), cipher.AsSpan(0, read), tag);

            BinaryPrimitives.WriteInt32LittleEndian(header, read);
            await output.WriteAsync(header, ct).ConfigureAwait(false);
            await output.WriteAsync(nonce, ct).ConfigureAwait(false);
            await output.WriteAsync(cipher.AsMemory(0, read), ct).ConfigureAwait(false);
            await output.WriteAsync(tag, ct).ConfigureAwait(false);
        }
    }

    public static async Task DecryptFileAsync(
        string sourcePath,
        string targetPath,
        string password,
        CancellationToken ct = default)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var magic = new byte[Magic.Length];
        await ReadExactAsync(input, magic, ct).ConfigureAwait(false);
        if (!magic.SequenceEqual(Magic))
        {
            throw new InvalidDataException("Die Datei ist kein Pixel-Backup-Archiv.");
        }

        var salt = new byte[SaltSize];
        await ReadExactAsync(input, salt, ct).ConfigureAwait(false);
        var key = DeriveKey(password, salt);

        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var aes = new AesGcm(key, TagSize);

        var header = new byte[4];
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[ChunkSize];
        var plain = new byte[ChunkSize];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var headerRead = await input.ReadAsync(header.AsMemory(0, 4), ct).ConfigureAwait(false);
            if (headerRead == 0)
            {
                break;
            }

            if (headerRead < 4)
            {
                throw new InvalidDataException("Das Archiv ist unvollständig.");
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length <= 0 || length > ChunkSize)
            {
                throw new InvalidDataException("Das Archiv ist beschädigt.");
            }

            await ReadExactAsync(input, nonce, ct).ConfigureAwait(false);
            await ReadExactAsync(input, cipher.AsMemory(0, length), ct).ConfigureAwait(false);
            await ReadExactAsync(input, tag, ct).ConfigureAwait(false);

            try
            {
                aes.Decrypt(nonce, cipher.AsSpan(0, length), tag, plain.AsSpan(0, length));
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("Falsches Kennwort oder beschädigtes Archiv.");
            }

            await output.WriteAsync(plain.AsMemory(0, length), ct).ConfigureAwait(false);
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            32);

    private static Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct) =>
        ReadExactAsync(stream, buffer.AsMemory(), ct);

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new InvalidDataException("Das Archiv ist unvollständig.");
            }

            offset += read;
        }
    }
}
