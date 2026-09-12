using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;
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
    private string _planSummary = "Noch nichts analysiert.";
    private string _targetDescription = string.Empty;

    public BackupViewModel(AppSession session)
    {
        _session = session;
        Title = "Sichern";
        Icon = "💾";

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
                UpdateTargetDescription();
                RaiseCommandStates();
            }
        };

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
        StatusMessage = $"Analyse abgeschlossen: {plan.SummaryText}";
    }

    private async Task<BackupPlan?> BuildPlanAsync()
    {
        if (!CanOperate() || _session.SelectedDevice is null)
        {
            StatusMessage = "Bitte zuerst ein Gerät verbinden.";
            return null;
        }

        var categories = SelectedCategories();
        if (categories.Count == 0)
        {
            StatusMessage = "Es ist keine Gruppe ausgewählt.";
            return null;
        }

        IsRunning = true;
        Progress.Reset("Analyse läuft");
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
            StatusMessage = "Analyse abgebrochen.";
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
            StatusMessage = "Bitte zuerst ein Gerät verbinden.";
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
                "Nichts zu tun",
                "Es wurden keine neuen oder geänderten Elemente gefunden. Die Sicherung ist bereits aktuell.");
            return;
        }

        if (CreateArchive && ArchivePassword.Length is > 0 and < 6)
        {
            await DialogService.ShowInfoAsync(
                "Kennwort zu kurz",
                "Bitte ein Kennwort mit mindestens sechs Zeichen verwenden – oder das Feld leer lassen.");
            return;
        }

        IsRunning = true;
        _session.IsBusy = true;
        _session.BusyDescription = "Sicherung läuft";
        ResultText = string.Empty;
        Progress.Reset("Sicherung wird vorbereitet");
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
                ? "Abgebrochen – " + result.SummaryText
                : result.SummaryText;

            StatusMessage = ResultText;

            if (_session.Settings.KeepSetsPerDevice > 0)
            {
                var removed = _session.Repository.ApplyRetention(serial, _session.Settings.KeepSetsPerDevice);
                if (removed > 0)
                {
                    StatusMessage += $" · {removed} alte Sicherung(en) entfernt";
                }
            }

            _session.Settings.SelectedCategories = SelectedCategories().Select(c => c.Id).ToList();
            _session.SaveSettings();
            _session.NotifyBackupsChanged();
            _plan = null;

            var message = result.SummaryText +
                          Environment.NewLine + Environment.NewLine +
                          "Ordner: " + result.SetDirectory +
                          (result.ArchivePath is null ? string.Empty : Environment.NewLine + "Archiv: " + result.ArchivePath) +
                          (result.Warnings.Count == 0
                              ? string.Empty
                              : Environment.NewLine + Environment.NewLine + "Hinweise:" + Environment.NewLine +
                                string.Join(Environment.NewLine, result.Warnings.Distinct().Take(15)));

            await DialogService.ShowInfoAsync(result.Canceled ? "Sicherung abgebrochen" : "Sicherung abgeschlossen", message);
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

        _session.Log.Info("Automatische Sicherung wird gestartet.");
        _plan = null;
        await StartAsync().ConfigureAwait(true);
    }

    private List<BackupCategory> SelectedCategories() =>
        Categories.Where(c => c.IsSelected).Select(c => c.Category).ToList();

    private void SetSelection(Func<CategoryItemViewModel, bool> predicate)
    {
        foreach (var item in Categories)
        {
            item.IsSelected = predicate(item);
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
        PlanSummary = "Noch nichts analysiert.";
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
            TargetDescription = "Zielordner: " + _session.Settings.BackupRoot;
            return;
        }

        var latest = _session.Repository.LatestFor(serial);
        TargetDescription = Incremental && latest is not null
            ? $"Aktualisiert den vorhandenen Satz „{latest.Name}“ vom {latest.UpdatedText} ({latest.SizeText})."
            : "Legt einen neuen Sicherungssatz unter " + _session.Settings.BackupRoot + " an.";
    }

    private void RaiseCommandStates()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error("Sicherung fehlgeschlagen", ex);
        StatusMessage = "Fehler: " + ex.Message;
        _ = DialogService.ShowInfoAsync("Fehler", ex.Message);
    }
}
