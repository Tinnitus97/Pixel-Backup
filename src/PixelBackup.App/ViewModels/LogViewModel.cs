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
        Icon = "📜";
        UpdateTitle();

        ClearCommand = new RelayCommand(() =>
        {
            _session.LogEntries.Clear();
            StatusMessage = Tr("Anzeige geleert – die Protokolldateien bleiben erhalten.",
                "Display cleared – the log files are kept.");
        });

        OpenFolderCommand = new RelayCommand(() => DialogService.OpenInFileManager(_session.LogDirectory));
        SaveCommand = new AsyncRelayCommand(SaveAsync, null, ex => StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message);
    }

    protected override void UpdateTitle() => Title = Tr("Protokoll", "Log");

    public AppSession Session => _session;

    #region Beschriftungen

    public string LabelSave => Tr("Protokoll speichern", "Save log");

    public string LabelOpenFolder => Tr("Protokollordner öffnen", "Open log folder");

    public string LabelClear => Tr("Anzeige leeren", "Clear display");

    #endregion

    public IReadOnlyList<LogEntry> Entries => _session.LogEntries;

    public RelayCommand ClearCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public string LogPathText => Tr("Protokolldateien: ", "Log files: ") + _session.LogDirectory;

    private async Task SaveAsync()
    {
        var folder = await DialogService.PickFolderAsync(Tr("Zielordner für das Protokoll", "Target folder for the log"));
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
        StatusMessage = Tr("Protokoll gespeichert: ", "Log saved: ") + path;
    }
}
