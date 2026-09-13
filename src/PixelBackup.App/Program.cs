using Avalonia;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Services;

namespace PixelBackup.App;

internal static class Program
{
    // Avalonia benötigt einen STA-Thread und darf vor AppMain keine Avalonia-Typen anfassen.
    [STAThread]
    public static int Main(string[] args)
    {
        // Unter Linux zuerst klären, ob überhaupt eine Anzeige da ist. Sonst
        // bricht Avalonia mit einer Meldung ab, mit der niemand etwas anfangen kann.
        if (OperatingSystem.IsLinux())
        {
            Localizer.I.Lang = Localizer.DetectFromSystem();

            var problem = LinuxEnvironment.DisplayProblem;
            if (problem is not null)
            {
                Console.Error.WriteLine($"Pixel Backup: {problem}");
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
            Console.Error.WriteLine($"Pixel Backup: {ex.Message}");

            if (OperatingSystem.IsLinux())
            {
                Console.Error.WriteLine(Loc.Tr(
                    $"Grafische Sitzung: {LinuxEnvironment.SessionText}. Avalonia zeichnet unter Wayland über XWayland.",
                    $"Graphical session: {LinuxEnvironment.SessionText}. Avalonia draws through XWayland on Wayland."));
            }

            return 1;
        }
    }

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
