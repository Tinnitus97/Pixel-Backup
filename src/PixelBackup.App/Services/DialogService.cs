using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using PixelBackup.App.Views;

namespace PixelBackup.App.Services;

/// <summary>Kleine Helfer für Dateiauswahl, Hinweisfenster und den Dateimanager.</summary>
public static class DialogService
{
    public static Window? MainWindow { get; set; }

    public static async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        if (MainWindow is null)
        {
            return null;
        }

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        };

        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
        {
            options.SuggestedStartLocation = await MainWindow.StorageProvider.TryGetFolderFromPathAsync(startPath!);
        }

        var folders = await MainWindow.StorageProvider.OpenFolderPickerAsync(options);
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    public static async Task<string?> PickFileAsync(string title, string? startPath = null)
    {
        if (MainWindow is null)
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("adb")
                {
                    Patterns = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? new[] { "adb.exe" }
                        : new[] { "adb", "*" }
                },
                FilePickerFileTypes.All
            }
        };

        if (!string.IsNullOrWhiteSpace(startPath) && Directory.Exists(startPath))
        {
            options.SuggestedStartLocation = await MainWindow.StorageProvider.TryGetFolderFromPathAsync(startPath!);
        }

        var files = await MainWindow.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    public static Task ShowInfoAsync(string title, string message) =>
        MessageDialog.ShowAsync(MainWindow, title, message, confirm: false);

    public static async Task<bool> ConfirmAsync(string title, string message) =>
        await MessageDialog.ShowAsync(MainWindow, title, message, confirm: true);

    /// <summary>Öffnet eine Adresse im Standardbrowser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Ohne Browser (z. B. auf einem Server) passiert schlicht nichts.
        }
    }

    /// <summary>Öffnet einen Ordner oder eine Datei im Dateimanager des Systems.</summary>
    public static void OpenInFileManager(string path)
    {
        try
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception)
        {
            // Ohne Dateimanager (z. B. auf einem Server) passiert schlicht nichts.
        }
    }
}
