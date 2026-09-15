using System.Runtime.InteropServices;
using Avalonia;
using PixelBackup.App.Cli;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Services;

namespace PixelBackup.App;

internal static class Program
{
    // Avalonia benötigt einen STA-Thread und darf vor AppMain keine Avalonia-Typen anfassen.
    [STAThread]
    public static int Main(string[] args)
    {
        // Mit einem Befehl arbeitet das Programm auf der Kommandozeile, ohne
        // Argumente startet die Oberfläche.
        var options = CommandLine.Parse(args);
        if (options.Command != CliCommand.Gui)
        {
            AttachToConsole();
            return RunCli(options);
        }

        // Unter Linux zuerst klären, ob überhaupt eine Anzeige da ist. Sonst
        // bricht Avalonia mit einer Meldung ab, mit der niemand etwas anfangen kann.
        if (OperatingSystem.IsLinux())
        {
            Localizer.I.Lang = Localizer.DetectFromSystem();

            var problem = LinuxEnvironment.DisplayProblem;
            if (problem is not null)
            {
                Console.Error.WriteLine($"Pixel Backup: {problem}");
                Console.Error.WriteLine(CliHelp.Usage);
                return 78; // EX_CONFIG
            }
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception ex)
        {
            // Fehlt eine der erst zur Laufzeit geladenen X11-Bibliotheken, steht ihr
            // Name in der Meldung. Dann statt der seitenlangen Suchliste von .NET
            // einen Satz mit dem passenden Paket der Verteilung ausgeben.
            var missing = OperatingSystem.IsLinux()
                ? LinuxEnvironment.DescribeMissingLibrary(ex.ToString())
                : null;

            Console.Error.WriteLine($"Pixel Backup: {missing ?? ex.Message}");

            if (OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine(Loc.Tr(
                    $"Grafische Sitzung: {LinuxEnvironment.SessionText}",
                    $"Graphical session: {LinuxEnvironment.SessionText}"));
            }

            return 1;
        }
    }

    private static int RunCli(CliOptions options)
    {
        using var cancellation = new CancellationTokenSource();

        // Strg+C soll den laufenden Vorgang geordnet beenden, nicht abwürgen.
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };

        return new CliRunner(options).RunAsync(cancellation.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Windows startet die Anwendung als Fensterprogramm (WinExe) – sie hängt
    /// dann an keiner Konsole, und jede Ausgabe ginge ins Leere. Deshalb wird
    /// die Konsole des aufrufenden Fensters übernommen; gibt es keine (Start per
    /// Doppelklick), bleibt alles wie bisher.
    /// </summary>
    private static void AttachToConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            if (!AttachConsole(AttachParentProcess))
            {
                return;
            }

            // Die Ströme zeigen nach dem Anhängen noch ins Leere; sie werden neu geöffnet.
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch (Exception)
        {
            // Ohne Konsole läuft der Befehl trotzdem – nur eben ohne Ausgabe.
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new X11PlatformOptions
        {
            // Gilt für X11 und für XWayland: Dateiauswahl und Menüs laufen über
            // die Portale des Desktops, damit sie unter Wayland-Sitzungen
            // (GNOME, KDE, Sway) genauso aussehen und funktionieren wie unter X11.
            UseDBusFilePicker = true,
            UseDBusMenu = true,
            EnableIme = true,
            EnableMultiTouch = true,
        })
        .WithInterFont()
        .LogToTrace();
}
