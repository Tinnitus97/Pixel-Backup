using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.Core.Services;

/// <summary>Prüft, ob ein Sicherungssatz vollständig und unbeschädigt auf der Platte liegt.</summary>
public sealed class VerificationService
{
    private readonly ILogSink _log;

    public VerificationService(ILogSink? log = null) => _log = log ?? NullLogSink.Instance;

    public async Task<VerificationResult> VerifyAsync(
        BackupSet set,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();
        var entries = set.Manifest.Entries;
        var bytesTotal = entries.Sum(e => Math.Max(0, e.Size));
        long bytesDone = 0;
        var index = 0;

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            result.Checked++;

            progress?.Report(new OperationProgress
            {
                Phase = Loc.Tr("Sicherung wird geprüft", "Verifying backup"),
                CurrentItem = entry.RelativePath,
                ItemsDone = index,
                ItemsTotal = entries.Count,
                BytesDone = bytesDone,
                BytesTotal = bytesTotal
            });

            var path = set.LocalPathOf(entry);
            if (!File.Exists(path))
            {
                result.Missing.Add(entry.RelativePath);
                continue;
            }

            var info = new FileInfo(path);
            bytesDone += info.Length;

            if (entry.Size >= 0 && info.Length != entry.Size)
            {
                result.SizeMismatch.Add(entry.RelativePath);
                continue;
            }

            if (string.IsNullOrEmpty(entry.Sha256))
            {
                result.WithoutHash++;
                result.Ok++;
                continue;
            }

            var hash = await Hashing.Sha256FileAsync(path, ct).ConfigureAwait(false);
            if (!string.Equals(hash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                result.HashMismatch.Add(entry.RelativePath);
            }
            else
            {
                result.Ok++;
            }
        }

        _log.Info(Loc.Tr("Überprüfung beendet: ", "Verification finished: ") + result.SummaryText);
        return result;
    }
}
