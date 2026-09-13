using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.App.ViewModels;

/// <summary>Seite "Sichern": Gruppen auswählen, analysieren und Sicherung starten.</summary>
public sealed class BackupViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private CancellationTokenSource? _cancellation;
    private BackupPlan? _plan;

    private bool _isRunning;
    private bool _incremental;
    private bool _computeHashes;
    private bool _createArchive;
    private bool _removeDeletedFiles;
    private string _archivePassword = string.Empty;
    private string _resultText = string.Empty;
    private string _planSummary = Loc.Tr("Noch nichts analysiert.", "Nothing analysed yet.");
    private string _targetDescription = string.Empty;

    public BackupViewModel(AppSession session)
    {
        _session = session;
        Icon = "💾";
        UpdateTitle();

        _incremental = session.Settings.IncrementalByDefault;
        _computeHashes = session.Settings.ComputeHashes;
        _createArchive = session.Settings.CreateArchive;
        _removeDeletedFiles = session.Settings.RemoveDeletedFiles;

        foreach (var category in CategoryCatalog.All)
        {
            var selected = session.Settings.SelectedCategories.Count > 0
                ? session.Settings.SelectedCategories.Contains(category.Id)
                : category.SelectedByDefault;

            var item = new CategoryItemViewModel(category, selected);
            item.SelectionChanged += OnSelectionChanged;
            Categories.Add(item);
        }

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, CanOperate, ReportError);
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanOperate() && AnySelected, ReportError);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsRunning);
        SelectAllCommand = new RelayCommand(() => SetSelection(_ => true));
        SelectNoneCommand = new RelayCommand(() => SetSelection(_ => false));
        SelectRecommendedCommand = new RelayCommand(() => SetSelection(c => c.Category.SelectedByDefault));

        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSession.SelectedDevice) or nameof(AppSession.DeviceInfo))
            {
                ResetPlan();
                UpdateAvailability();
                UpdateTargetDescription();
                RaiseCommandStates();
            }
        };

        UpdateAvailability();
        UpdateTargetDescription();
    }

    protected override void UpdateTitle() => Title = Tr("Sichern", "Back up");

    /// <summary>Beschriftungen auch in den Gruppenzeilen auffrischen.</summary>
    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var item in Categories)
        {
            item.RefreshTexts();
        }

        // Gespeicherte Sätze stehen noch in der alten Sprache und müssen neu gebildet werden.
        PlanSummary = _plan is null ? Tr("Noch nichts analysiert.", "Nothing analysed yet.") : _plan.SummaryText;
        UpdateTargetDescription();
    }

    public AppSession Session => _session;

    public ObservableCollection<CategoryItemViewModel> Categories { get; } = new();

    public OperationProgressViewModel Progress { get; } = new();

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public RelayCommand SelectRecommendedCommand { get; }

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

    public bool Incremental
    {
        get => _incremental;
        set
        {
            if (SetProperty(ref _incremental, value))
            {
                _session.Settings.IncrementalByDefault = value;
                ResetPlan();
                UpdateTargetDescription();
            }
        }
    }

    public bool ComputeHashes
    {
        get => _computeHashes;
        set
        {
            if (SetProperty(ref _computeHashes, value))
            {
                _session.Settings.ComputeHashes = value;
            }
        }
    }

    public bool CreateArchive
    {
        get => _createArchive;
        set
        {
            if (SetProperty(ref _createArchive, value))
            {
                _session.Settings.CreateArchive = value;
            }
        }
    }

    public bool RemoveDeletedFiles
    {
        get => _removeDeletedFiles;
        set
        {
            if (SetProperty(ref _removeDeletedFiles, value))
            {
                _session.Settings.RemoveDeletedFiles = value;
            }
        }
    }

    public string ArchivePassword
    {
        get => _archivePassword;
        set => SetProperty(ref _archivePassword, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        private set => SetProperty(ref _planSummary, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set
        {
            if (SetProperty(ref _resultText, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(ResultText);

    public string TargetDescription
    {
        get => _targetDescription;
        private set => SetProperty(ref _targetDescription, value);
    }

    public bool AnySelected => Categories.Any(c => c.IsSelected);

    // ------------------------------------------------------------------ Aktionen

    public override Task ActivateAsync()
    {
        UpdateAvailability();
        UpdateTargetDescription();
        RaiseCommandStates();
        return Task.CompletedTask;
    }

    private bool CanOperate() => _session.HasDevice && _session.AdbAvailable && !IsRunning;

    private async Task AnalyzeAsync()
    {
        var plan = await BuildPlanAsync().ConfigureAwait(true);
        if (plan is null)
        {
            return;
        }

        PlanSummary = plan.SummaryText;
        StatusMessage = Tr($"Analyse abgeschlossen: {plan.SummaryText}", $"Analysis finished: {plan.SummaryText}");
    }

    private async Task<BackupPlan?> BuildPlanAsync()
    {
        if (!CanOperate() || _session.SelectedDevice is null)
        {
            StatusMessage = Tr("Bitte zuerst ein Gerät verbinden.", "Please connect a device first.");
            return null;
        }

        var categories = SelectedCategories();
        if (categories.Count == 0)
        {
            StatusMessage = Tr("Es ist keine Gruppe ausgewählt.", "No group is selected.");
            return null;
        }

        IsRunning = true;
        Progress.Reset(Tr("Analyse läuft", "Analysing"));
        Progress.IsIndeterminate = true;
        ResultText = string.Empty;

        _cancellation = new CancellationTokenSource();

        try
        {
            if (_session.DeviceInfo is null)
            {
                await _session.RefreshDeviceInfoAsync().ConfigureAwait(true);
            }

            var device = _session.DeviceInfo ?? new DeviceInfo { Serial = _session.SelectedDevice.Serial };
            var target = Incremental ? _session.Repository.LatestFor(device.Serial) : null;

            var status = new Progress<string>(text => StatusMessage = text);
            var service = _session.CreateBackupService();

            _plan = await service
                .CreatePlanAsync(_session.SelectedDevice.Serial, device, categories, target, status, _cancellation.Token)
                .ConfigureAwait(true);

            foreach (var item in Categories)
            {
                var summary = _plan.Summaries.FirstOrDefault(s => s.CategoryId == item.Id);
                if (item.IsSelected)
                {
                    item.ApplyAnalysis(summary?.ItemCount ?? 0, summary?.Bytes ?? 0);
                }
                else
                {
                    item.ResetAnalysis();
                }
            }

            PlanSummary = _plan.SummaryText;
            return _plan;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Tr("Analyse abgebrochen.", "Analysis cancelled.");
            return null;
        }
        finally
        {
            Progress.IsIndeterminate = false;
            Progress.Reset();
            IsRunning = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private async Task StartAsync()
    {
        if (!CanOperate() || _session.SelectedDevice is null)
        {
            StatusMessage = Tr("Bitte zuerst ein Gerät verbinden.", "Please connect a device first.");
            return;
        }

        var plan = _plan;
        if (plan is null)
        {
            plan = await BuildPlanAsync().ConfigureAwait(true);
            if (plan is null)
            {
                return;
            }
        }

        if (plan.CopyCount == 0)
        {
            await DialogService.ShowInfoAsync(
                Tr("Nichts zu tun", "Nothing to do"),
                Tr("Es wurden keine neuen oder geänderten Elemente gefunden. Die Sicherung ist bereits aktuell.",
                   "No new or changed items were found. The backup is already up to date."));
            return;
        }

        if (CreateArchive && ArchivePassword.Length is > 0 and < 6)
        {
            await DialogService.ShowInfoAsync(
                Tr("Kennwort zu kurz", "Password too short"),
                Tr("Bitte ein Kennwort mit mindestens sechs Zeichen verwenden – oder das Feld leer lassen.",
                   "Please use a password of at least six characters – or leave the field empty."));
            return;
        }

        IsRunning = true;
        _session.IsBusy = true;
        _session.BusyDescription = Tr("Sicherung läuft", "Backup running");
        ResultText = string.Empty;
        Progress.Reset(Tr("Sicherung wird vorbereitet", "Preparing backup"));
        _cancellation = new CancellationTokenSource();

        try
        {
            var serial = _session.SelectedDevice.Serial;
            var target = Incremental ? _session.Repository.LatestFor(serial) : null;

            var options = new BackupOptions
            {
                BackupRoot = _session.Settings.BackupRoot,
                Incremental = Incremental && target is not null,
                TargetSet = target,
                ComputeHashes = ComputeHashes,
                CreateArchive = CreateArchive,
                ArchivePassword = string.IsNullOrEmpty(ArchivePassword) ? null : ArchivePassword,
                RemoveDeletedFiles = RemoveDeletedFiles
            };

            var progress = new Progress<OperationProgress>(p => Progress.Update(p));
            var service = _session.CreateBackupService();
            var result = await service
                .RunAsync(serial, plan, options, progress, _cancellation.Token)
                .ConfigureAwait(true);

            ResultText = result.Canceled
                ? Tr("Abgebrochen – ", "Cancelled – ") + result.SummaryText
                : result.SummaryText;

            StatusMessage = ResultText;

            if (_session.Settings.KeepSetsPerDevice > 0)
            {
                var removed = _session.Repository.ApplyRetention(serial, _session.Settings.KeepSetsPerDevice);
                if (removed > 0)
                {
                    StatusMessage += Tr($" · {removed} alte Sicherung(en) entfernt", $" · {removed} old backup(s) removed");
                }
            }

            _session.Settings.SelectedCategories = SelectedCategories().Select(c => c.Id).ToList();
            _session.SaveSettings();
            _session.NotifyBackupsChanged();
            _plan = null;

            var message = result.SummaryText +
                          Environment.NewLine + Environment.NewLine +
                          Tr("Ordner: ", "Folder: ") + result.SetDirectory +
                          (result.ArchivePath is null
                              ? string.Empty
                              : Environment.NewLine + Tr("Archiv: ", "Archive: ") + result.ArchivePath) +
                          (result.Warnings.Count == 0
                              ? string.Empty
                              : Environment.NewLine + Environment.NewLine + Tr("Hinweise:", "Notes:") + Environment.NewLine +
                                string.Join(Environment.NewLine, result.Warnings.Distinct().Take(15)));

            await DialogService.ShowInfoAsync(
                result.Canceled
                    ? Tr("Sicherung abgebrochen", "Backup cancelled")
                    : Tr("Sicherung abgeschlossen", "Backup finished"),
                message);
        }
        finally
        {
            IsRunning = false;
            _session.IsBusy = false;
            _session.BusyDescription = string.Empty;
            Progress.Reset();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    /// <summary>Sicherung ohne Rückfragen – wird beim automatischen Start verwendet.</summary>
    public async Task RunAutomaticBackupAsync()
    {
        if (IsRunning || !CanOperate())
        {
            return;
        }

        _session.Log.Info(Tr("Automatische Sicherung wird gestartet.", "Starting automatic backup."));
        _plan = null;
        await StartAsync().ConfigureAwait(true);
    }

    private List<BackupCategory> SelectedCategories() =>
        Categories.Where(c => c.IsSelected).Select(c => c.Category).ToList();

    private void SetSelection(Func<CategoryItemViewModel, bool> predicate)
    {
        foreach (var item in Categories)
        {
            item.IsSelected = item.IsAvailable && predicate(item);
        }
    }

    /// <summary>
    /// Sperrt Gruppen, die auf dem angeschlossenen Gerät nicht möglich sind – aktuell die
    /// vollständige App-Daten-Sicherung, die Root-Rechte voraussetzt.
    /// </summary>
    private void UpdateAvailability()
    {
        var device = _session.DeviceInfo;
        var hasRoot = device?.HasRoot ?? false;
        var sdk = device?.SdkNumber ?? 0;

        foreach (var item in Categories)
        {
            if (item.Category.RequiresRoot)
            {
                item.SetAvailability(
                    hasRoot,
                    hasRoot
                        ? string.Empty
                        : Tr(
                        "Nicht verfügbar: Das Gerät bietet keinen Root-Zugriff. Ohne Root lässt Android seit " +
                        "Version 12 keine Sicherung fremder App-Daten zu.",
                        "Not available: this device offers no root access. Without root, Android has blocked " +
                        "backups of other apps' data since version 12."));
                continue;
            }

            // Der alte adb-backup-Weg ist ab Android 13 faktisch abgeschaltet.
            if (item.Id == "appdata" && sdk >= 33)
            {
                item.SetAvailability(
                    false,
                    Tr(
                        $"Nicht verfügbar: Ab Android 13 (dieses Gerät: API {sdk}) liefert „adb backup“ keine Daten mehr. " +
                        "Nutze stattdessen „Apps (APK)“ und beim Einrichten des neuen Telefons die Android-Übertragung.",
                        $"Not available: from Android 13 on (this device: API {sdk}) \"adb backup\" returns no data. " +
                        "Use \"Apps (APK)\" instead, plus the Android transfer when setting up the new phone."));
                continue;
            }

            if (!item.IsAvailable)
            {
                item.SetAvailability(true, string.Empty);
            }
        }
    }

    private void OnSelectionChanged()
    {
        ResetPlan();
        OnPropertyChanged(nameof(AnySelected));
        RaiseCommandStates();
    }

    private void ResetPlan()
    {
        _plan = null;
        PlanSummary = Tr("Noch nichts analysiert.", "Nothing analysed yet.");
        foreach (var item in Categories)
        {
            item.ResetAnalysis();
        }
    }

    private void UpdateTargetDescription()
    {
        var serial = _session.SelectedDevice?.Serial;
        if (serial is null)
        {
            TargetDescription = Tr("Zielordner: ", "Target folder: ") + _session.Settings.BackupRoot;
            return;
        }

        var latest = _session.Repository.LatestFor(serial);
        TargetDescription = Incremental && latest is not null
            ? Tr($"Aktualisiert den vorhandenen Satz „{latest.Name}“ vom {latest.UpdatedText} ({latest.SizeText}).",
                 $"Updates the existing set \"{latest.Name}\" from {latest.UpdatedText} ({latest.SizeText}).")
            : Tr("Legt einen neuen Sicherungssatz unter " + _session.Settings.BackupRoot + " an.",
                 "Creates a new backup set under " + _session.Settings.BackupRoot + ".");
    }

    private void RaiseCommandStates()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    #region Beschriftungen

    public string PageHint => Tr(
        "Gruppen auswählen, analysieren und die Sicherung starten. Fotos, Videos, Apps und mehr landen als lesbare Dateien auf dem PC.",
        "Pick the groups, analyse them and start the backup. Photos, videos, apps and more end up as readable files on the PC.");

    public string LabelIncremental => Tr("Nur Neues und Geändertes sichern", "Back up new and changed items only");

    public string TipIncremental => Tr(
        "Aktualisiert den letzten Sicherungssatz dieses Gerätes, statt alles erneut zu übertragen.",
        "Updates this device's most recent backup set instead of transferring everything again.");

    public string LabelHashes => Tr("Prüfsummen berechnen", "Calculate checksums");

    public string TipHashes => Tr(
        "Ermöglicht später eine echte Überprüfung der Sicherung (SHA-256).",
        "Enables a real verification of the backup later on (SHA-256).");

    public string LabelRemoveDeleted => Tr("Gelöschte Dateien entfernen", "Remove deleted files");

    public string TipRemoveDeleted => Tr(
        "Entfernt beim inkrementellen Lauf Kopien, die es auf dem Gerät nicht mehr gibt.",
        "During an incremental run this removes copies that no longer exist on the device.");

    public string LabelArchive => Tr("Am Ende ZIP-Archiv erstellen", "Create a ZIP archive at the end");

    public string LabelArchivePassword => Tr("Kennwort für das Archiv (optional):", "Password for the archive (optional):");

    public string HintArchivePassword => Tr("AES-256, mindestens 6 Zeichen", "AES-256, at least 6 characters");

    public string LabelSelectAll => Tr("Alle", "All");

    public string LabelSelectNone => Tr("Keine", "None");

    public string LabelSelectRecommended => Tr("Empfohlene Auswahl", "Recommended selection");

    public string LabelAnalyze => Tr("Analysieren", "Analyse");

    public string LabelStart => Tr("Sicherung starten", "Start backup");

    public string LabelCancel => Tr("Abbrechen", "Cancel");

    #endregion

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Sicherung fehlgeschlagen", "Backup failed"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
        _ = DialogService.ShowInfoAsync(Tr("Fehler", "Error"), ex.Message);
    }
}
