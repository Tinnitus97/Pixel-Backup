using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.Core.Services;

/// <summary>Was beim Einspielen herausgekommen ist.</summary>
public sealed record UpdateApplyResult(bool Success, string Message, bool RestartsItself = false)
{
    /// <summary>Die Anwendung muss sich jetzt beenden, damit der Austausch laufen kann.</summary>
    public bool ShouldExit => Success && RestartsItself;
}

/// <summary>
/// Spielt eine heruntergeladene Fassung ein. Wie das geht, hängt davon ab,
/// woher die laufende Fassung stammt:
///
/// * EXE, AppImage, portable Fassung – die Datei wird ausgetauscht. Weil sich
///   eine laufende Datei nicht selbst überschreiben kann, erledigt das ein
///   kleines Skript: es wartet auf das Ende dieses Vorgangs, ersetzt die Datei
///   und startet das Programm wieder.
/// * .deb, .rpm, Flatpak – dafür ist die Paketverwaltung zuständig. Sie wird
///   mit dem heruntergeladenen Paket aufgerufen (unter Rückfrage nach dem
///   Kennwort), danach genügt ein Neustart des Programms.
/// </summary>
public sealed class UpdateInstaller
{
    private readonly ILogSink _log;

    public UpdateInstaller(ILogSink? log = null) => _log = log ?? NullLogSink.Instance;

    /// <summary>Spielt die geladene Datei ein.</summary>
    public async Task<UpdateApplyResult> ApplyAsync(
        UpdatePackage package,
        string downloadedFile,
        CancellationToken ct = default)
    {
        if (!File.Exists(downloadedFile))
        {
            return new UpdateApplyResult(false, Loc.Tr(
                "Die heruntergeladene Datei ist nicht mehr da.",
                "The downloaded file is gone."));
        }

        try
        {
            return package.Kind switch
            {
                InstallationKind.WindowsExe => SwapExecutable(downloadedFile),
                InstallationKind.AppImage => SwapExecutable(downloadedFile),
                InstallationKind.Portable => SwapExtractedBinary(
                    await ExtractBinaryAsync(downloadedFile, ct).ConfigureAwait(false), downloadedFile),
                InstallationKind.Deb or InstallationKind.Rpm or InstallationKind.Flatpak =>
                    await RunPackageManagerAsync(package.Kind, downloadedFile, ct).ConfigureAwait(false),
                _ => new UpdateApplyResult(false, Loc.Tr(
                    "Für diese Einbauart gibt es keinen automatischen Weg.",
                    "There is no automatic path for this installation kind."))
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Error(Loc.Tr("Die neue Fassung konnte nicht eingespielt werden", "The new version could not be installed"), ex);
            return new UpdateApplyResult(false, ex.Message);
        }
    }

    // ------------------------------------------------------------ Datei tauschen

    /// <summary>Tauscht die aus dem Archiv geholte Datei ein; das Archiv wird nicht mehr gebraucht.</summary>
    private UpdateApplyResult SwapExtractedBinary(string binary, string archive)
    {
        var result = SwapExecutable(binary);

        if (result.Success && !string.Equals(binary, archive, StringComparison.Ordinal))
        {
            try
            {
                File.Delete(archive);
            }
            catch (IOException)
            {
                // Bleibt im Zwischenspeicher liegen – stört niemanden.
            }
        }

        return result;
    }

    private UpdateApplyResult SwapExecutable(string newFile)
    {
        var target = CurrentTarget();
        if (target is null)
        {
            return new UpdateApplyResult(false, Loc.Tr(
                "Die laufende Programmdatei ist nicht auffindbar.",
                "The running program file could not be determined."));
        }

        if (!CanWrite(target))
        {
            return new UpdateApplyResult(false, Loc.Tr(
                $"Keine Schreibrechte für {target}. Die neue Fassung liegt hier: {newFile}",
                $"No write permission for {target}. The new version is here: {newFile}"));
        }

        Directory.CreateDirectory(UpdateService.WorkFolder);
        var pid = Environment.ProcessId;

        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(UpdateService.WorkFolder, "austausch.cmd");
            File.WriteAllText(script, BuildWindowsScript(newFile, target, pid), Encoding.UTF8);
            Start("cmd.exe", new[] { "/c", script });
        }
        else
        {
            var script = Path.Combine(UpdateService.WorkFolder, "austausch.sh");
            File.WriteAllText(script, BuildUnixScript(newFile, target, pid), Encoding.UTF8);
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Start("/bin/sh", new[] { script });
        }

        _log.Info(Loc.Tr(
            "Die neue Fassung wird nach dem Beenden eingespielt und das Programm neu gestartet.",
            "The new version is installed after exit and the program restarts."));

        return new UpdateApplyResult(true, Loc.Tr(
            "Pixel Backup beendet sich jetzt, tauscht die Datei aus und startet neu.",
            "Pixel Backup now exits, swaps the file and restarts."), RestartsItself: true);
    }

    /// <summary>Die Datei, die ersetzt werden soll.</summary>
    private static string? CurrentTarget()
    {
        // Aus einer AppImage heraus zeigt APPIMAGE auf das Abbild selbst; die
        // laufende Datei liegt im entpackten Abbild und wäre das falsche Ziel.
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        return !string.IsNullOrEmpty(appImage) ? appImage : Environment.ProcessPath;
    }

    private static bool CanWrite(string file)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(file));
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var probe = Path.Combine(directory, $".pixelbackup-{Guid.NewGuid():n}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Wartet auf das Ende des Programms, ersetzt die Datei, startet neu.</summary>
    public static string BuildWindowsScript(string newFile, string targetFile, int processId) =>
        $"""
         @echo off
         rem Wartet auf das Ende von Pixel Backup und ersetzt dann die Datei.
         setlocal
         set "NEU={newFile}"
         set "ZIEL={targetFile}"

         for /l %%i in (1,1,30) do (
           tasklist /fi "PID eq {processId}" 2>nul | find "{processId}" >nul || goto ersetzen
           timeout /t 1 /nobreak >nul
         )

         :ersetzen
         move /y "%NEU%" "%ZIEL%" >nul
         if errorlevel 1 (
           echo.
           echo Die Datei konnte nicht ersetzt werden.
           echo Neue Fassung liegt hier: %NEU%
           echo Ziel: %ZIEL%
           echo.
           pause
           exit /b 1
         )

         start "" "%ZIEL%"
         del "%~f0" >nul 2>&1
         """;

    /// <summary>Dasselbe für Linux und macOS.</summary>
    public static string BuildUnixScript(string newFile, string targetFile, int processId) =>
        $"""
         #!/bin/sh
         # Wartet auf das Ende von Pixel Backup und ersetzt dann die Datei.
         NEU='{newFile}'
         ZIEL='{targetFile}'

         i=0
         while [ $i -lt 30 ] && kill -0 {processId} 2>/dev/null; do
           sleep 1
           i=$((i + 1))
         done

         if ! mv -f "$NEU" "$ZIEL"; then
           echo "Die Datei konnte nicht ersetzt werden."
           echo "Neue Fassung liegt hier: $NEU"
           echo "Ziel: $ZIEL"
           exit 1
         fi

         chmod +x "$ZIEL"
         "$ZIEL" &
         rm -f "$0"
         """;

    // ------------------------------------------------------- Paketverwaltung

    private async Task<UpdateApplyResult> RunPackageManagerAsync(
        InstallationKind kind,
        string file,
        CancellationToken ct)
    {
        var command = BuildPackageCommand(kind, file, LinuxEnvironment.PackageManager, InSandbox);
        if (command is null)
        {
            return new UpdateApplyResult(false, Loc.Tr(
                $"Kein passender Befehl gefunden. Die Datei liegt hier: {file}",
                $"No suitable command found. The file is here: {file}"));
        }

        _log.Info(Loc.Tr(
            $"Einspielen mit: {command.Value.FileName} {string.Join(' ', command.Value.Arguments)}",
            $"Installing with: {command.Value.FileName} {string.Join(' ', command.Value.Arguments)}"));

        var result = await ProcessRunner
            .RunAsync(command.Value.FileName, command.Value.Arguments, ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return new UpdateApplyResult(false, Loc.Tr(
                $"Die Paketverwaltung hat abgebrochen: {result.ErrorSummary}",
                $"The package manager stopped: {result.ErrorSummary}"));
        }

        return new UpdateApplyResult(true, Loc.Tr(
            "Eingespielt. Die neue Fassung ist nach einem Neustart des Programms aktiv.",
            "Installed. The new version is active after restarting the program."));
    }

    /// <summary>Läuft die Anwendung in einem Flatpak-Behälter?</summary>
    public static bool InSandbox =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")) || File.Exists("/.flatpak-info");

    /// <summary>
    /// Der Befehl, mit dem sich das geladene Paket einspielen lässt. Getrennt
    /// von der Ausführung, damit er sich prüfen lässt.
    /// <paramref name="inSandbox"/>: aus einem Flatpak heraus muss der Aufruf
    /// über flatpak-spawn auf dem Wirt landen.
    /// </summary>
    public static (string FileName, string[] Arguments)? BuildPackageCommand(
        InstallationKind kind,
        string file,
        LinuxPackageManager packageManager,
        bool inSandbox = false)
    {
        string[]? parts = kind switch
        {
            InstallationKind.Deb => new[] { "pkexec", "apt-get", "install", "-y", file },
            InstallationKind.Rpm => packageManager switch
            {
                LinuxPackageManager.Zypper => new[] { "pkexec", "zypper", "--non-interactive", "install", "--allow-unsigned-rpm", file },
                _ => new[] { "pkexec", "dnf", "install", "-y", file }
            },
            // Ein Bündel wird für den angemeldeten Benutzer eingespielt – dafür
            // braucht es keine erhöhten Rechte.
            InstallationKind.Flatpak => new[] { "flatpak", "install", "--user", "--bundle", "-y", file },
            _ => null
        };

        if (parts is null)
        {
            return null;
        }

        if (inSandbox)
        {
            parts = new[] { "flatpak-spawn", "--host" }.Concat(parts).ToArray();
        }

        return (parts[0], parts.Skip(1).ToArray());
    }

    // ------------------------------------------------------------ tar.gz auspacken

    /// <summary>
    /// Holt die Programmdatei aus dem portablen Archiv. Übernommen wird
    /// ausschließlich der Eintrag „PixelBackup“ – aus einem manipulierten
    /// Archiv kann so nichts anderes auf der Platte landen.
    /// </summary>
    public static async Task<string> ExtractBinaryAsync(string archive, CancellationToken ct = default)
    {
        if (!archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            && !archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            // Schon eine fertige Datei.
            return archive;
        }

        var target = Path.Combine(UpdateService.WorkFolder, "PixelBackup");
        Directory.CreateDirectory(UpdateService.WorkFolder);

        await using var file = File.OpenRead(archive);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);

        while (await tar.GetNextEntryAsync(cancellationToken: ct).ConfigureAwait(false) is { } entry)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
            {
                continue;
            }

            if (!IsProgramEntry(entry.Name))
            {
                continue;
            }

            await entry.ExtractToFileAsync(target, overwrite: true, ct).ConfigureAwait(false);

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            return target;
        }

        throw new InvalidDataException(Loc.Tr(
            "Das Archiv enthält keine Programmdatei.",
            "The archive contains no program file."));
    }

    /// <summary>Nur „…/PixelBackup“ – kein Pfad mit ".." und nichts anderes.</summary>
    public static bool IsProgramEntry(string entryName)
    {
        var normalized = entryName.Replace('\\', '/').TrimStart('/');

        if (normalized.Contains("../", StringComparison.Ordinal))
        {
            return false;
        }

        var name = normalized[(normalized.LastIndexOf('/') + 1)..];
        return name is "PixelBackup" or "PixelBackup.exe";
    }

    private static void Start(string fileName, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = UpdateService.WorkFolder
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        Process.Start(info);
    }
}
