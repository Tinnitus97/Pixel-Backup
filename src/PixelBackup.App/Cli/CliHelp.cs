using System.Reflection;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Services;

namespace PixelBackup.App.Cli;

/// <summary>
/// Hilfe und Handbuch für die Kommandozeile. Das ausführliche Handbuch ist die
/// Textfassung der Manpage und liegt als eingebettete Datei bei – damit sagt
/// „pixel-backup --manual“ unter Windows dasselbe wie „man pixel-backup“ unter
/// Linux, ohne dass dort ein man-Programm nötig wäre.
/// </summary>
public static class CliHelp
{
    public const string ManualResource = "PixelBackup.App.Cli.pixel-backup.1.txt";

    /// <summary>
    /// Der Name, unter dem das Programm gerade läuft – unter Windows
    /// „PixelBackup.exe“, sonst „pixel-backup“. So passen die Beispiele in der
    /// Hilfe zu dem, was man wirklich tippen muss.
    /// </summary>
    public static string ProgramName
    {
        get
        {
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    var name = Path.GetFileName(path);
                    var plain = Path.GetFileNameWithoutExtension(path);

                    // Aus einem Paket heraus liegt die Datei in einem Ordner
                    // "pixel-backup" und wird über den Starter gleichen Namens
                    // aufgerufen – dann ist das der Name, den man tippt.
                    var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
                    if (string.Equals(folder, "pixel-backup", StringComparison.Ordinal))
                    {
                        return "pixel-backup";
                    }

                    // Beim Aufruf über den Testläufer oder "dotnet run" steht dort
                    // etwas anderes; dann bleibt es beim üblichen Namen.
                    if (plain.StartsWith("PixelBackup", StringComparison.OrdinalIgnoreCase)
                        || plain.StartsWith("pixel-backup", StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch (Exception)
            {
                // Dann eben der Name aus dem Paket.
            }

            return OperatingSystem.IsWindows() ? "PixelBackup.exe" : "pixel-backup";
        }
    }

    public static string Usage => Loc.Tr(
        $"Aufruf: {ProgramName} [Befehl] [Optionen]  |  Hilfe: {ProgramName} --help",
        $"Usage: {ProgramName} [command] [options]  |  help: {ProgramName} --help");

    /// <summary>Die kurze Hilfe, die --help ausgibt.</summary>
    public static string Text => Loc.Tr(German, English);

    /// <summary>Das vollständige Handbuch (Textfassung der Manpage).</summary>
    public static string Manual
    {
        get
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(ManualResource);
                if (stream is null)
                {
                    return Text;
                }

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd().TrimEnd();
            }
            catch (Exception)
            {
                return Text;
            }
        }
    }

    private static string German => $"""
        Pixel Backup {InstallationInfo.CurrentVersion} – Sicherung für Android-Geräte über adb

        Aufruf:
          {ProgramName}                        startet die Oberfläche
          {ProgramName} <Befehl> [Optionen]

        Befehle:
          devices          verbundene Geräte auflisten
          info             Angaben zum Gerät (Android, Akku, Speicher, Root)
          categories       verfügbare Gruppen mit Kennung auflisten
          backup           Sicherung anlegen
          restore          Sicherung zurückspielen
          list             vorhandene Sicherungssätze auflisten
          verify           Sicherungssatz gegen die Prüfsummen prüfen
          components       adb und Gerätezugang prüfen (--install richtet ein)
          update           auf eine neue Fassung prüfen (--install spielt ein)
          gui              die Oberfläche starten
          help, version, manual

        Optionen:
          -d, --device <Seriennummer>   Gerät wählen (sonst das einzige verbundene)
          -o, --output <Ordner>         Speicherort der Sicherungen
          -c, --categories <a,b,c>      Gruppen (siehe „categories“)
          -a, --all                     alle Gruppen
          -s, --set <Name|Pfad>         Sicherungssatz (sonst der jüngste)
          -i, --incremental             vorhandenen Satz aktualisieren
              --no-hashes               ohne Prüfsummen (schneller)
              --archive                 zum Schluss ein Archiv anlegen
              --password <Kennwort>     Archiv verschlüsseln
              --conflict skip|overwrite|both   vorhandene Dateien am Gerät
              --dry-run                 nur planen, nichts schreiben
          -y, --yes                     Rückfragen bejahen
              --json                    Ausgabe als JSON
          -q, --quiet                   nur das Nötigste ausgeben
          -l, --language de|en          Sprache der Ausgabe
              --install                 bei components/update: einrichten

        Beispiele:
          {ProgramName} devices
          {ProgramName} backup --categories photos,videos --output D:\\Sicherungen
          {ProgramName} backup --all --incremental --yes
          {ProgramName} list --json
          {ProgramName} restore --set 2026-09-15_Pixel-8 --conflict skip --yes
          {ProgramName} verify --set 2026-09-15_Pixel-8

        Rückgabewerte: 0 in Ordnung, 1 Fehler, 2 Aufruffehler, 3 kein Gerät,
        4 adb fehlt, 130 abgebrochen.

        Ausführlich: {ProgramName} --manual   (unter Linux auch: man pixel-backup)
        """;

    private static string English => $"""
        Pixel Backup {InstallationInfo.CurrentVersion} – backup for Android devices over adb

        Usage:
          {ProgramName}                        starts the graphical interface
          {ProgramName} <command> [options]

        Commands:
          devices          list connected devices
          info             device details (Android, battery, storage, root)
          categories       list the available groups and their ids
          backup           create a backup
          restore          restore a backup
          list             list existing backup sets
          verify           verify a backup set against its checksums
          components       check adb and device access (--install sets it up)
          update           check for a new version (--install installs it)
          gui              start the graphical interface
          help, version, manual

        Options:
          -d, --device <serial>         choose the device (default: the only one)
          -o, --output <folder>         where the backups live
          -c, --categories <a,b,c>      groups (see "categories")
          -a, --all                     every group
          -s, --set <name|path>         backup set (default: the newest)
          -i, --incremental             update an existing set
              --no-hashes               skip checksums (faster)
              --archive                 create an archive afterwards
              --password <password>     encrypt the archive
              --conflict skip|overwrite|both   existing files on the device
              --dry-run                 plan only, write nothing
          -y, --yes                     answer prompts with yes
              --json                    print JSON
          -q, --quiet                   print only what is necessary
          -l, --language de|en          language of the output
              --install                 components/update: install

        Examples:
          {ProgramName} devices
          {ProgramName} backup --categories photos,videos --output /mnt/disk
          {ProgramName} backup --all --incremental --yes
          {ProgramName} list --json
          {ProgramName} restore --set 2026-09-15_Pixel-8 --conflict skip --yes
          {ProgramName} verify --set 2026-09-15_Pixel-8

        Exit codes: 0 fine, 1 error, 2 usage, 3 no device, 4 adb missing,
        130 cancelled.

        In detail: {ProgramName} --manual   (on Linux also: man pixel-backup)
        """;
}
