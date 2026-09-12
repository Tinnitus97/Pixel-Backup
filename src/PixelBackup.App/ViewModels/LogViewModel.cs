using System.Text;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;

namespace PixelBackup.App.ViewModels;

/// <summary>Seite "Protokoll": zeigt alle Meldungen und speichert sie auf Wunsch als Datei.</summary>
public sealed class LogViewModel : ViewModelBase
{
    private readonly AppSession _session;

    public LogViewModel(AppSession session)
    {
        _session = session;
        Title = "Protokoll";
        Icon = "📜";

        ClearCommand = new RelayCommand(() =>
        {
            _session.LogEntries.Clear();
            StatusMessage = "Anzeige geleert – die Protokolldateien bleiben erhalten.";
        });

        OpenFolderCommand = new RelayCommand(() => DialogService.OpenInFileManager(_session.LogDirectory));
        SaveCommand = new AsyncRelayCommand(SaveAsync, null, ex => StatusMessage = "Fehler: " + ex.Message);
    }

    public AppSession Session => _session;

    public IReadOnlyList<LogEntry> Entries => _session.LogEntries;

    public RelayCommand ClearCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public string LogPathText => "Protokolldateien: " + _session.LogDirectory;

    private async Task SaveAsync()
    {
        var folder = await DialogService.PickFolderAsync("Zielordner für das Protokoll");
        if (folder is null)
        {
            return;
        }

        var path = Path.Combine(folder, $"pixel-backup-protokoll-{DateTime.Now:yyyy-MM-dd_HH-mm}.txt");
        var builder = new StringBuilder();
        foreach (var entry in _session.LogEntries)
        {
            builder.AppendLine(entry.ToString());
        }

        await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8);
        StatusMessage = "Protokoll gespeichert: " + path;
    }
}
