using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PixelBackup.App.Services;
using PixelBackup.App.ViewModels;
using PixelBackup.App.Views;

namespace PixelBackup.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = new AppSession();
            var viewModel = new MainWindowViewModel(session);

            desktop.MainWindow = new MainWindow { DataContext = viewModel };
            desktop.ShutdownRequested += (_, _) => session.Shutdown();

            // Erst nach dem Erzeugen des Fensters starten, damit Meldungen sichtbar sind.
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
