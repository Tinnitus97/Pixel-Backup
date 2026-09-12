using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
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
        Icon = "🗃";
        UpdateTitle();

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

    protected override void UpdateTitle() => Title = Tr("Sicherungen", "Backups");

    public override void RefreshTexts()
    {
        base.RefreshTexts();
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
                return Tr("Noch kein Sicherungssatz ausgewählt.", "No backup set selected yet.");
            }

            var manifest = SelectedSet.Manifest;
            var lines = new List<string>
            {
                Tr($"Gerät: {manifest.Device.DisplayName} ({manifest.Device.Serial})",
                   $"Device: {manifest.Device.DisplayName} ({manifest.Device.Serial})"),
                $"Android: {manifest.Device.AndroidText}",
                Tr($"Erstellt: {SelectedSet.CreatedText} · aktualisiert: {SelectedSet.UpdatedText}",
                   $"Created: {SelectedSet.CreatedText} · updated: {SelectedSet.UpdatedText}"),
                Tr($"Umfang: {Humanize.Items(manifest.FileCount)} · {SelectedSet.SizeText}",
                   $"Size: {Humanize.Items(manifest.FileCount)} · {SelectedSet.SizeText}"),
                Tr($"Ordner: {SelectedSet.Directory}", $"Folder: {SelectedSet.Directory}"),
                string.Empty,
                Tr("Inhalt:", "Contents:")
            };

            foreach (var group in manifest.Entries.GroupBy(e => e.CategoryId))
            {
                lines.Add(
                    $"   {CategoryCatalog.DisplayNameOf(group.Key)}: {Humanize.Items(group.Count())}, {Humanize.Bytes(group.Sum(e => Math.Max(0, e.Size)))}");
            }

            if (manifest.Runs.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add(Tr("Läufe:", "Runs:"));
                foreach (var run in manifest.Runs.OrderByDescending(r => r.StartedUtc).Take(8))
                {
                    lines.Add(
                        $"   {run.StartedUtc.ToLocalTime():g} · " +
                        (run.Mode == BackupMode.Incremental ? Tr("inkrementell", "incremental") : Tr("vollständig", "full")) +
                        Tr($" · {run.FilesCopied:N0} kopiert, ", $" · {run.FilesCopied:N0} copied, ") +
                        $"{Humanize.Bytes(run.BytesCopied)}, {Humanize.Duration(run.Duration)}" +
                        (run.Canceled ? Tr(" (abgebrochen)", " (cancelled)") : string.Empty));
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
        DiskUsageText = Tr(
            $"{Humanize.Count(Sets.Count, "Sicherungssatz", "Sicherungssätze")} · " +
            $"{Humanize.Bytes(_session.Repository.CalculateDiskUsage())} auf der Festplatte · " +
            _session.Settings.BackupRoot,
            $"{Humanize.Count(Sets.Count, "backup set", "backup sets")} · " +
            $"{Humanize.Bytes(_session.Repository.CalculateDiskUsage())} on disk · " +
            _session.Settings.BackupRoot);
    }

    private async Task DeleteAsync()
    {
        var set = SelectedSet;
        if (set is null)
        {
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(
            Tr("Sicherung löschen", "Delete backup"),
            Tr($"Der Sicherungssatz „{set.Name}“ ({set.SizeText}) wird unwiderruflich von der Festplatte gelöscht.",
               $"The backup set \"{set.Name}\" ({set.SizeText}) will be deleted from the disk for good.") +
            Environment.NewLine + set.Directory);

        if (!confirmed)
        {
            return;
        }

        _session.Repository.Delete(set);
        Reload();
        StatusMessage = Tr("Sicherungssatz gelöscht.", "Backup set deleted.");
    }

    private async Task VerifyAsync()
    {
        var set = SelectedSet;
        if (set is null)
        {
            return;
        }

        IsRunning = true;
        Progress.Reset(Tr("Prüfung läuft", "Verification running"));
        _cancellation = new CancellationTokenSource();

        try
        {
            var progress = new Progress<OperationProgress>(p => Progress.Update(p));
            var result = await _session.CreateVerificationService()
                .VerifyAsync(set, progress, _cancellation.Token)
                .ConfigureAwait(true);

            StatusMessage = result.SummaryText;

            var details = new List<string> { result.SummaryText };
            AppendProblems(details, Tr("Fehlende Dateien", "Missing files"), result.Missing);
            AppendProblems(details, Tr("Abweichende Größe", "Different size"), result.SizeMismatch);
            AppendProblems(details, Tr("Abweichende Prüfsumme", "Different checksum"), result.HashMismatch);

            await DialogService.ShowInfoAsync(Tr("Prüfergebnis", "Verification result"), string.Join(Environment.NewLine, details));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr("Prüfung abgebrochen.", "Verification cancelled.");
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
            lines.Add(Loc.Tr($"   … und {items.Count - 10} weitere", $"   … and {items.Count - 10} more"));
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
        Progress.Reset(Tr("Archiv wird erstellt", "Creating archive"));
        Progress.IsIndeterminate = true;
        _cancellation = new CancellationTokenSource();

        try
        {
            var path = await ArchiveService
                .CreateArchiveAsync(set.Directory, password: null, _cancellation.Token)
                .ConfigureAwait(true);

            StatusMessage = Tr("Archiv erstellt: ", "Archive created: ") + path;
            await DialogService.ShowInfoAsync(Tr("Archiv erstellt", "Archive created"), path);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr("Archivierung abgebrochen.", "Archiving cancelled.");
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

    #region Beschriftungen

    public string LabelRefresh => Tr("Aktualisieren", "Refresh");

    public string LabelOpenFolder => Tr("Ordner öffnen", "Open folder");

    public string LabelVerify => Tr("Sicherung prüfen", "Verify backup");

    public string LabelArchive => Tr("Als ZIP archivieren", "Archive as ZIP");

    public string LabelDelete => Tr("Löschen", "Delete");

    public string LabelCancel => Tr("Abbrechen", "Cancel");

    #endregion

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Aktion fehlgeschlagen", "Action failed"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
        _ = DialogService.ShowInfoAsync(Tr("Fehler", "Error"), ex.Message);
    }
}
