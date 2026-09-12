using Avalonia;

namespace PixelBackup.App;

internal static class Program
{
    // Avalonia benötigt einen STA-Thread und darf vor AppMain keine Avalonia-Typen anfassen.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
