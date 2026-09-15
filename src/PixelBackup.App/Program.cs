using System.Runtime.InteropServices;
using System.Text;
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
            PrepareConsole();
            return RunCli(options);
        }

        // Ohne Befehl: das Konsolenfenster verschwindet, es bleibt das Fenster
        // der Anwendung. Wurde sie aus einer vorhandenen Konsole gestartet,
        // bleibt diese unangetastet.
        HideOwnConsole();

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
    /// Stellt die Konsole auf UTF-8. Ohne das zeigt die Eingabeaufforderung
    /// unter Windows für „·“, Umlaute und Anführungszeichen wirres Zeug an,
    /// weil sie noch mit einer alten Codepage arbeitet.
    /// </summary>
    private static void PrepareConsole()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception)
        {
            // Ohne Konsole (Aufruf aus einem Dienst) bleibt es bei der Vorgabe.
        }
    }

    /// <summary>
    /// Blendet das eigene Konsolenfenster aus. Das ist nur der Fall, wenn die
    /// Anwendung per Doppelklick gestartet wurde – dann hängt kein anderer
    /// Vorgang an dieser Konsole. Aus einer Eingabeaufforderung heraus hängen
    /// mindestens zwei daran; die bleibt selbstverständlich stehen.
    /// </summary>
    private static void HideOwnConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var processes = new uint[4];
            if (GetConsoleProcessList(processes, (uint)processes.Length) != 1)
            {
                return;
            }

            var window = GetConsoleWindow();
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SwHide);
            }

            FreeConsole();
        }
        catch (Exception)
        {
            // Bleibt das Fenster stehen, ist das unschön, aber nicht schlimm.
        }
    }

    private const int SwHide = 0;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

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
