using PixelBackup.Core.Localization;

namespace PixelBackup.App.Cli;

/// <summary>Die Befehle der Kommandozeile.</summary>
public enum CliCommand
{
    /// <summary>Kein Befehl – die Oberfläche startet.</summary>
    Gui,

    Help,
    Version,
    Manual,
    Devices,
    Info,
    Categories,
    Backup,
    Restore,
    List,
    Verify,
    Components,
    Update
}

/// <summary>
/// Eine ausgewertete Kommandozeile. Das Auswerten ist bewusst von der
/// Ausführung getrennt: So lässt sich jede Schreibweise prüfen, ohne dass ein
/// Gerät, adb oder eine Oberfläche nötig wäre.
/// </summary>
public sealed class CliOptions
{
    public CliCommand Command { get; init; } = CliCommand.Gui;

    /// <summary>Seriennummer des Geräts (--device); leer = das einzige verbundene.</summary>
    public string? Device { get; init; }

    /// <summary>Ordner für die Sicherungen (--output); leer = der eingestellte.</summary>
    public string? Output { get; init; }

    /// <summary>Gewählte Gruppen (--categories); leer = die empfohlenen.</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();

    /// <summary>Alle Gruppen statt der empfohlenen (--all).</summary>
    public bool AllCategories { get; init; }

    /// <summary>Ein bestimmter Sicherungssatz (--set): Ordnername oder Pfad.</summary>
    public string? Set { get; init; }

    public bool Incremental { get; init; }

    public bool NoHashes { get; init; }

    public bool Archive { get; init; }

    public string? Password { get; init; }

    /// <summary>Umgang mit vorhandenen Dateien beim Zurückspielen (--conflict).</summary>
    public string Conflict { get; init; } = "skip";

    /// <summary>Nur planen und melden, nichts schreiben (--dry-run).</summary>
    public bool DryRun { get; init; }

    /// <summary>Rückfragen mit „ja“ beantworten (--yes).</summary>
    public bool AssumeYes { get; init; }

    /// <summary>Ausgabe als JSON statt als Text (--json).</summary>
    public bool Json { get; init; }

    /// <summary>Nur das Nötigste ausgeben (--quiet).</summary>
    public bool Quiet { get; init; }

    /// <summary>Sprache der Ausgabe (--language de|en); leer = die des Systems.</summary>
    public string? Language { get; init; }

    /// <summary>Bei „update“: die neue Fassung auch einspielen (--install).</summary>
    public bool Install { get; init; }

    /// <summary>Fehlermeldung des Auswertens; null, wenn alles in Ordnung ist.</summary>
    public string? Error { get; init; }

    public bool IsValid => Error is null;

    public static CliOptions Failed(string error) => new() { Command = CliCommand.Help, Error = error };
}

/// <summary>Wertet die Kommandozeile aus.</summary>
public static class CommandLine
{
    /// <summary>Die Namen, unter denen ein Befehl angesprochen werden kann.</summary>
    private static readonly Dictionary<string, CliCommand> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gui"] = CliCommand.Gui,
        ["help"] = CliCommand.Help,
        ["hilfe"] = CliCommand.Help,
        ["version"] = CliCommand.Version,
        ["manual"] = CliCommand.Manual,
        ["handbuch"] = CliCommand.Manual,
        ["devices"] = CliCommand.Devices,
        ["geraete"] = CliCommand.Devices,
        ["info"] = CliCommand.Info,
        ["categories"] = CliCommand.Categories,
        ["gruppen"] = CliCommand.Categories,
        ["backup"] = CliCommand.Backup,
        ["sichern"] = CliCommand.Backup,
        ["restore"] = CliCommand.Restore,
        ["wiederherstellen"] = CliCommand.Restore,
        ["list"] = CliCommand.List,
        ["liste"] = CliCommand.List,
        ["verify"] = CliCommand.Verify,
        ["pruefen"] = CliCommand.Verify,
        ["components"] = CliCommand.Components,
        ["komponenten"] = CliCommand.Components,
        ["update"] = CliCommand.Update
    };

    /// <summary>Alle Befehlsnamen in der Reihenfolge der Hilfe.</summary>
    public static IReadOnlyList<string> CommandNames { get; } = new[]
    {
        "devices", "info", "categories", "backup", "restore",
        "list", "verify", "components", "update", "gui", "help", "version", "manual"
    };

    /// <summary>
    /// Wertet die Argumente aus. Ohne Argumente kommt <see cref="CliCommand.Gui"/>
    /// zurück – ein Doppelklick startet also weiterhin die Oberfläche.
    /// </summary>
    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return new CliOptions { Command = CliCommand.Gui };
        }

        var command = CliCommand.Gui;
        var commandSeen = false;

        string? device = null, output = null, set = null, password = null, language = null;
        var categories = new List<string>();
        var conflict = "skip";
        bool all = false, incremental = false, noHashes = false, archive = false;
        bool dryRun = false, assumeYes = false, json = false, quiet = false, install = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];

            // Der Wert einer Option darf auch mit Gleichheitszeichen kommen.
            string? Inline(out bool used)
            {
                var equals = arg.IndexOf('=');
                if (equals > 0)
                {
                    used = false;
                    return arg[(equals + 1)..];
                }

                used = true;
                return i + 1 < args.Count ? args[++i] : null;
            }

            var name = arg.Split('=')[0];

            switch (name)
            {
                case "-h" or "--help":
                    command = CliCommand.Help;
                    commandSeen = true;
                    break;

                case "-v" or "--version":
                    command = CliCommand.Version;
                    commandSeen = true;
                    break;

                case "--manual":
                    command = CliCommand.Manual;
                    commandSeen = true;
                    break;

                case "-d" or "--device":
                    device = Inline(out _);
                    if (device is null)
                    {
                        return Missing("--device");
                    }

                    break;

                case "-o" or "--output":
                    output = Inline(out _);
                    if (output is null)
                    {
                        return Missing("--output");
                    }

                    break;

                case "-c" or "--categories":
                    var list = Inline(out _);
                    if (list is null)
                    {
                        return Missing("--categories");
                    }

                    categories.AddRange(list
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;

                case "-s" or "--set":
                    set = Inline(out _);
                    if (set is null)
                    {
                        return Missing("--set");
                    }

                    break;

                case "--conflict":
                    var mode = Inline(out _);
                    if (mode is null)
                    {
                        return Missing("--conflict");
                    }

                    if (mode is not ("skip" or "overwrite" or "both"))
                    {
                        return CliOptions.Failed(Loc.Tr(
                            $"Unbekannte Konfliktbehandlung: {mode} (skip, overwrite oder both)",
                            $"Unknown conflict mode: {mode} (skip, overwrite or both)"));
                    }

                    conflict = mode;
                    break;

                case "--password":
                    password = Inline(out _);
                    if (password is null)
                    {
                        return Missing("--password");
                    }

                    break;

                case "-l" or "--language":
                    language = Inline(out _);
                    if (language is null)
                    {
                        return Missing("--language");
                    }

                    if (language is not ("de" or "en"))
                    {
                        return CliOptions.Failed(Loc.Tr(
                            $"Unbekannte Sprache: {language} (de oder en)",
                            $"Unknown language: {language} (de or en)"));
                    }

                    break;

                case "-a" or "--all":
                    all = true;
                    break;

                case "-i" or "--incremental":
                    incremental = true;
                    break;

                case "--no-hashes":
                    noHashes = true;
                    break;

                case "--archive":
                    archive = true;
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "-y" or "--yes":
                    assumeYes = true;
                    break;

                case "--json":
                    json = true;
                    break;

                case "-q" or "--quiet":
                    quiet = true;
                    break;

                case "--install":
                    install = true;
                    break;

                default:
                    if (name.StartsWith('-'))
                    {
                        return CliOptions.Failed(Loc.Tr(
                            $"Unbekannte Option: {name}",
                            $"Unknown option: {name}"));
                    }

                    if (commandSeen)
                    {
                        return CliOptions.Failed(Loc.Tr(
                            $"Zu viele Befehle: {name}",
                            $"Too many commands: {name}"));
                    }

                    if (!Commands.TryGetValue(name, out command))
                    {
                        return CliOptions.Failed(Loc.Tr(
                            $"Unbekannter Befehl: {name}",
                            $"Unknown command: {name}"));
                    }

                    commandSeen = true;
                    break;
            }
        }

        return new CliOptions
        {
            Command = commandSeen ? command : CliCommand.Help,
            Device = device,
            Output = output,
            Categories = categories,
            AllCategories = all,
            Set = set,
            Incremental = incremental,
            NoHashes = noHashes,
            Archive = archive,
            Password = password,
            Conflict = conflict,
            DryRun = dryRun,
            AssumeYes = assumeYes,
            Json = json,
            Quiet = quiet,
            Language = language,
            Install = install
        };
    }

    /// <summary>Soll die Oberfläche starten? Das ist der Fall ohne Argumente und bei „gui“.</summary>
    public static bool WantsGui(IReadOnlyList<string> args) => Parse(args).Command == CliCommand.Gui;

    private static CliOptions Missing(string option) => CliOptions.Failed(Loc.Tr(
        $"Der Option {option} fehlt der Wert.",
        $"Option {option} needs a value."));
}
