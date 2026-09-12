using System.Text;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Services;

/// <summary>
/// Erzeugt aus den Rohdaten-Exporten eines Sicherungssatzes die Dateien, mit denen sich
/// Kontakte, Nachrichten und Termine auf einem <b>neuen Telefon</b> einspielen lassen.
/// Läuft am Ende jeder Sicherung und lässt sich auf bestehende Sätze erneut anwenden.
/// </summary>
public static class ImportFileBuilder
{
    public const string ImportRemotePrefix = "import:";

    public static async Task<List<BackupEntry>> BuildAsync(
        string setDirectory,
        IReadOnlyCollection<BackupEntry> entries,
        ILogSink? log = null,
        CancellationToken ct = default)
    {
        log ??= NullLogSink.Instance;
        var created = new List<BackupEntry>();

        foreach (var category in CategoryCatalog.All.Where(c => c.Import != ImportFormat.None))
        {
            ct.ThrowIfCancellationRequested();

            var exports = entries
                .Where(e => e.Type == BackupEntryType.ContentExport &&
                            string.Equals(e.CategoryId, category.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (exports.Count == 0)
            {
                continue;
            }

            var rowsByUri = new List<(string Uri, List<Dictionary<string, string>> Rows)>();
            foreach (var export in exports)
            {
                var path = PathMapper.ToLocalPath(setDirectory, export.RelativePath);
                if (!File.Exists(path))
                {
                    continue;
                }

                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                rowsByUri.Add((export.RemotePath, ContentRowParser.Parse(text)));
            }

            if (rowsByUri.Count == 0)
            {
                continue;
            }

            var (fileName, content) = Build(category.Import, rowsByUri);
            if (content is null || content.Length == 0)
            {
                continue;
            }

            var relativePath = $"{PathMapper.DataFolder}/{fileName}";
            var target = PathMapper.ToLocalPath(setDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content, new UTF8Encoding(false), ct).ConfigureAwait(false);

            var info = new FileInfo(target);
            created.Add(new BackupEntry
            {
                CategoryId = category.Id,
                Type = BackupEntryType.ImportFile,
                RemotePath = ImportRemotePrefix + fileName,
                RelativePath = relativePath,
                Size = info.Length,
                Sha256 = await Hashing.Sha256FileAsync(target, ct).ConfigureAwait(false)
            });

            log.Info(Loc.Tr(
                $"Importdatei erzeugt: {fileName} ({Humanize.Bytes(info.Length)})",
                $"Import file created: {fileName} ({Humanize.Bytes(info.Length)})"));
        }

        return created;
    }

    private static (string FileName, string? Content) Build(
        ImportFormat format,
        IReadOnlyList<(string Uri, List<Dictionary<string, string>> Rows)> rowsByUri)
    {
        List<Dictionary<string, string>> RowsFor(string uriPart)
        {
            foreach (var item in rowsByUri)
            {
                if (item.Uri.Contains(uriPart, StringComparison.OrdinalIgnoreCase))
                {
                    return item.Rows;
                }
            }

            return new List<Dictionary<string, string>>();
        }

        List<Dictionary<string, string>> AllRows() =>
            rowsByUri.SelectMany(i => i.Rows).ToList();

        switch (format)
        {
            case ImportFormat.VCard:
            {
                var phones = RowsFor("phones");
                if (phones.Count == 0)
                {
                    phones = AllRows();
                }

                var mails = RowsFor("emails");
                return phones.Count == 0
                    ? ("kontakte.vcf", null)
                    : ("kontakte.vcf", ImportExporters.BuildVCards(phones, mails));
            }

            case ImportFormat.SmsXml:
            {
                var rows = RowsFor("content://sms");
                if (rows.Count == 0)
                {
                    rows = AllRows();
                }

                return rows.Count == 0 ? ("sms.xml", null) : ("sms.xml", ImportExporters.BuildSmsXml(rows));
            }

            case ImportFormat.CallsXml:
            {
                var rows = AllRows();
                return rows.Count == 0
                    ? ("anrufliste.xml", null)
                    : ("anrufliste.xml", ImportExporters.BuildCallsXml(rows));
            }

            case ImportFormat.Ics:
            {
                var rows = AllRows();
                return rows.Count == 0 ? ("kalender.ics", null) : ("kalender.ics", ImportExporters.BuildIcs(rows));
            }

            default:
                return (string.Empty, null);
        }
    }
}
