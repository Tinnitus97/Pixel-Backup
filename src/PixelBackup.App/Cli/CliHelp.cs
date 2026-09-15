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

    public static string Usage => Loc.Tr(
        "Aufruf: pixel-backup [Befehl] [Optionen]   ·   Hilfe: pixel-backup --help",
        "Usage: pixel-backup [command] [options]   ·   help: pixel-backup --help");

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
          pixel-backup                        startet die Oberfläche
          pixel-backup <Befehl> [Optionen]

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
          pixel-backup devices
          pixel-backup backup --categories photos,videos --output /mnt/platte
          pixel-backup backup --all --incremental --yes
          pixel-backup list --json
          pixel-backup restore --set 2026-09-15_Pixel-8 --conflict skip --yes
          pixel-backup verify --set 2026-09-15_Pixel-8

        Rückgabewerte: 0 in Ordnung, 1 Fehler, 2 Aufruffehler, 3 kein Gerät,
        4 adb fehlt, 130 abgebrochen.

        Ausführlich: pixel-backup --manual   (unter Linux auch: man pixel-backup)
        """;

    private static string English => $"""
        Pixel Backup {InstallationInfo.CurrentVersion} – backup for Android devices over adb

        Usage:
          pixel-backup                        starts the graphical interface
          pixel-backup <command> [options]

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
          pixel-backup devices
          pixel-backup backup --categories photos,videos --output /mnt/disk
          pixel-backup backup --all --incremental --yes
          pixel-backup list --json
          pixel-backup restore --set 2026-09-15_Pixel-8 --conflict skip --yes
          pixel-backup verify --set 2026-09-15_Pixel-8

        Exit codes: 0 fine, 1 error, 2 usage, 3 no device, 4 adb missing,
        130 cancelled.

        In detail: pixel-backup --manual   (on Linux also: man pixel-backup)
        """;
}
