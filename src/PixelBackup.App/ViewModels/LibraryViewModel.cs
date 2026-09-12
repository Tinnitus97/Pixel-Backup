using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Model;
using PixelBackup.Core.Services;
using PixelBackup.Core.Util;

namespace PixelBackup.App.ViewModels;

/// <summary>Seite "Sicherungen": Übersicht, Prüfung, Archivierung und Löschen der Sätze.</summary>
public sealed class LibraryViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private CancellationTokenSource? _cancellation;
    private BackupSet? _selectedSet;
    private string _diskUsageText = string.Empty;
    private bool _isRunning;

    public LibraryViewModel(AppSession session)
    {
        _session = session;
        Title = "Sicherungen";
        Icon = "🗃";

        RefreshCommand = new RelayCommand(Reload);
        OpenFolderCommand = new RelayCommand(
            () => DialogService.OpenInFileManager(SelectedSet!.Directory),
            () => SelectedSet is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => SelectedSet is not null && !IsRunning, ReportError);
        VerifyCommand = new AsyncRelayCommand(VerifyAsync, () => SelectedSet is not null && !IsRunning, ReportError);
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => SelectedSet is not null && !IsRunning, ReportError);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsRunning);

        _session.BackupsChanged += Reload;
        Reload();
    }

    public AppSession Session => _session;

    public ObservableCollection<BackupSet> Sets { get; } = new();

    public OperationProgressViewModel Progress { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand VerifyCommand { get; }

    public AsyncRelayCommand ArchiveCommand { get; }

    public RelayCommand CancelCommand { get; }

    public BackupSet? SelectedSet
    {
        get => _selectedSet;
        set
        {
            if (SetProperty(ref _selectedSet, value))
            {
                OnPropertiesChanged(nameof(HasSet), nameof(Details));
                RaiseCommandStates();
            }
        }
    }

    public bool HasSet => SelectedSet is not null;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                RaiseCommandStates();
            }
        }
    }

    public bool IsIdle => !IsRunning;

    public string DiskUsageText
    {
        get => _diskUsageText;
        private set => SetProperty(ref _diskUsageText, value);
    }

    public string Details
    {
        get
        {
            if (SelectedSet is null)
            {
                return "Noch kein Sicherungssatz ausgewählt.";
            }

            var manifest = SelectedSet.Manifest;
            var lines = new List<string>
            {
                $"Gerät: {manifest.Device.DisplayName} ({manifest.Device.Serial})",
                $"Android: {manifest.Device.AndroidText}",
                $"Erstellt: {SelectedSet.CreatedText} · aktualisiert: {SelectedSet.UpdatedText}",
                $"Umfang: {Humanize.Count(manifest.FileCount, "Element", "Elemente")} · {SelectedSet.SizeText}",
                $"Ordner: {SelectedSet.Directory}",
                string.Empty,
                "Inhalt:"
            };

            foreach (var group in manifest.Entries.GroupBy(e => e.CategoryId))
            {
                lines.Add(
                    $"   {CategoryCatalog.DisplayNameOf(group.Key)}: {group.Count():N0} Elemente, {Humanize.Bytes(group.Sum(e => Math.Max(0, e.Size)))}");
            }

            if (manifest.Runs.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Läufe:");
                foreach (var run in manifest.Runs.OrderByDescending(r => r.StartedUtc).Take(8))
                {
                    lines.Add(
                        $"   {run.StartedUtc.ToLocalTime():dd.MM.yyyy HH:mm} · {(run.Mode == BackupMode.Incremental ? "inkrementell" : "vollständig")} · " +
                        $"{run.FilesCopied:N0} kopiert, {Humanize.Bytes(run.BytesCopied)}, {Humanize.Duration(run.Duration)}" +
                        (run.Canceled ? " (abgebrochen)" : string.Empty));
                }
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public override Task ActivateAsync()
    {
        Reload();
        return Task.CompletedTask;
    }

    private void Reload()
    {
        var previous = SelectedSet?.Directory;
        Sets.Clear();
        foreach (var set in _session.Repository.LoadSets())
        {
            Sets.Add(set);
        }

        SelectedSet = Sets.FirstOrDefault(s => s.Directory == previous) ?? Sets.FirstOrDefault();
        DiskUsageText = $"{Humanize.Count(Sets.Count, "Sicherungssatz", "Sicherungssätze")} · " +
                        $"{Humanize.Bytes(_session.Repository.CalculateDiskUsage())} auf der Festplatte · " +
                        _session.Settings.BackupRoot;
    }

    private async Task DeleteAsync()
    {
        var set = SelectedSet;
        if (set is null)
        {
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(
            "Sicherung löschen",
            $"Der Sicherungssatz „{set.Name}“ ({set.SizeText}) wird unwiderruflich von der Festplatte gelöscht." +
            Environment.NewLine + set.Directory);

        if (!confirmed)
        {
            return;
        }

        _session.Repository.Delete(set);
        Reload();
        StatusMessage = "Sicherungssatz gelöscht.";
    }

    private async Task VerifyAsync()
    {
        var set = SelectedSet;
        if (set is null)
        {
            return;
        }

        IsRunning = true;
        Progress.Reset("Prüfung läuft");
        _cancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<OperationProgress>(p => Progress.Update(p));
            var result = await _session.CreateVerificationService()
                .VerifyAsync(set, progress, _cancellation.Token)
                .ConfigureAwait(true);

            StatusMessage = result.SummaryText;

            var details = new List<string> { result.SummaryText };
            AppendProblems(details, "Fehlende Dateien", result.Missing);
            AppendProblems(details, "Abweichende Größe", result.SizeMismatch);
            AppendProblems(details, "Abweichende Prüfsumme", result.HashMismatch);

            await DialogService.ShowInfoAsync("Prüfergebnis", string.Join(Environment.NewLine, details));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Prüfung abgebrochen.";
        }
        finally
        {
            IsRunning = false;
            Progress.Reset();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private static void AppendProblems(List<string> lines, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"{title} ({items.Count}):");
        lines.AddRange(items.Take(10).Select(i => "   " + i));
        if (items.Count > 10)
        {
            lines.Add($"   … und {items.Count - 10} weitere");
        }
    }

    private async Task ArchiveAsync()
    {
        var set = SelectedSet;
        if (set is null)
        {
            return;
        }

        IsRunning = true;
        Progress.Reset("Archiv wird erstellt");
        Progress.IsIndeterminate = true;
        _cancellation = new CancellationTokenSource();

        try
        {
            var path = await ArchiveService
                .CreateArchiveAsync(set.Directory, password: null, _cancellation.Token)
                .ConfigureAwait(true);

            StatusMessage = "Archiv erstellt: " + path;
            await DialogService.ShowInfoAsync("Archiv erstellt", path);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Archivierung abgebrochen.";
        }
        finally
        {
            Progress.IsIndeterminate = false;
            IsRunning = false;
            Progress.Reset();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void RaiseCommandStates()
    {
        OpenFolderCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        VerifyCommand.RaiseCanExecuteChanged();
        ArchiveCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error("Aktion fehlgeschlagen", ex);
        StatusMessage = "Fehler: " + ex.Message;
        _ = DialogService.ShowInfoAsync("Fehler", ex.Message);
    }
}
