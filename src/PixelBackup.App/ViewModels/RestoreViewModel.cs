using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.App.ViewModels;

public sealed record ConflictOption(ConflictMode Mode, string DisplayName);

/// <summary>Eine Gruppe innerhalb eines Sicherungssatzes, die zurückgespielt werden kann.</summary>
public sealed class RestoreCategoryViewModel : ObservableObject
{
    private bool _isSelected = true;

    public RestoreCategoryViewModel(string categoryId, int itemCount, long bytes)
    {
        CategoryId = categoryId;
        ItemCount = itemCount;
        Bytes = bytes;
    }

    public string CategoryId { get; }

    public int ItemCount { get; }

    public long Bytes { get; }

    public string DisplayName => CategoryCatalog.DisplayNameOf(CategoryId);

    public string Icon => CategoryCatalog.ById(CategoryId)?.Icon ?? "📁";

    public string SummaryText => $"{Humanize.Items(ItemCount)} · {Humanize.Bytes(Bytes)}";

    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Seite "Wiederherstellen": Satz und Gruppen wählen und auf ein Gerät zurückspielen.</summary>
public sealed class RestoreViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private CancellationTokenSource? _cancellation;

    private BackupSet? _selectedSet;
    private ConflictOption _conflictOption;
    private bool _installApps = true;
    private bool _allowDowngrade;
    private bool _restoreLegacyAppData;
    private bool _restoreRootAppData = true;
    private bool _placeImportFiles = true;
    private bool _isRunning;
    private string _resultText = string.Empty;

    public RestoreViewModel(AppSession session)
    {
        _session = session;
        Icon = "♻";
        UpdateTitle();

        ConflictOptions = new ObservableCollection<ConflictOption>();
        BuildConflictOptions();
        _conflictOption = ConflictOptions[0];

        RefreshCommand = new RelayCommand(LoadSets);
        BrowseSourceCommand = new AsyncRelayCommand(BrowseSourceAsync, () => IsIdle, ReportError);
        OpenSourceCommand = new RelayCommand(() => DialogService.OpenInFileManager(BackupRoot));
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart, ReportError);
        CancelCommand = new RelayCommand(() => _cancellation?.Cancel(), () => IsRunning);
        SelectAllCommand = new RelayCommand(() => SetSelection(true));
        SelectNoneCommand = new RelayCommand(() => SetSelection(false));

        _session.BackupsChanged += LoadSets;
        _session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppSession.SelectedDevice))
            {
                RaiseCommandStates();
            }
        };

        LoadSets();
    }

    protected override void UpdateTitle() => Title = Tr("Wiederherstellen", "Restore");

    public override void RefreshTexts()
    {
        base.RefreshTexts();

        var selected = SelectedConflictOption.Mode;
        BuildConflictOptions();
        SelectedConflictOption = ConflictOptions.FirstOrDefault(o => o.Mode == selected) ?? ConflictOptions[0];

        foreach (var category in Categories)
        {
            category.RefreshTexts();
        }
    }

    private void BuildConflictOptions()
    {
        ConflictOptions.Clear();
        ConflictOptions.Add(new ConflictOption(ConflictMode.Skip, Tr("Vorhandene Dateien behalten", "Keep existing files")));
        ConflictOptions.Add(new ConflictOption(ConflictMode.Overwrite, Tr("Vorhandene Dateien überschreiben", "Overwrite existing files")));
        ConflictOptions.Add(new ConflictOption(ConflictMode.KeepBoth, Tr("Beide behalten (Namenszusatz)", "Keep both (name suffix)")));
    }

    public AppSession Session => _session;

    public ObservableCollection<BackupSet> Sets { get; } = new();

    public ObservableCollection<RestoreCategoryViewModel> Categories { get; } = new();

    public ObservableCollection<ConflictOption> ConflictOptions { get; }

    public OperationProgressViewModel Progress { get; } = new();

    public RelayCommand RefreshCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public BackupSet? SelectedSet
    {
        get => _selectedSet;
        set
        {
            if (SetProperty(ref _selectedSet, value))
            {
                UpdateCategories();
                OnPropertiesChanged(nameof(HasSet), nameof(SetDetails));
                RaiseCommandStates();
            }
        }
    }

    public bool HasSet => SelectedSet is not null;

    public string SetDetails => SelectedSet is null
        ? Tr("Bitte links einen Sicherungssatz auswählen.", "Please pick a backup set on the left.")
        : Tr($"{SelectedSet.DeviceName} · erstellt am {SelectedSet.CreatedText} · zuletzt aktualisiert {SelectedSet.UpdatedText}",
             $"{SelectedSet.DeviceName} · created {SelectedSet.CreatedText} · last updated {SelectedSet.UpdatedText}") +
          Environment.NewLine + SelectedSet.Summary +
          Environment.NewLine + SelectedSet.Directory;

    public ConflictOption SelectedConflictOption
    {
        get => _conflictOption;
        set => SetProperty(ref _conflictOption, value);
    }

    public bool InstallApps
    {
        get => _installApps;
        set => SetProperty(ref _installApps, value);
    }

    public bool AllowDowngrade
    {
        get => _allowDowngrade;
        set => SetProperty(ref _allowDowngrade, value);
    }

    public bool RestoreLegacyAppData
    {
        get => _restoreLegacyAppData;
        set => SetProperty(ref _restoreLegacyAppData, value);
    }

    /// <summary>Vollständige App-Daten zurückspielen – nur möglich, wenn beide Geräte Root bieten.</summary>
    public bool RestoreRootAppData
    {
        get => _restoreRootAppData;
        set => SetProperty(ref _restoreRootAppData, value);
    }

    /// <summary>Kontakte-, Nachrichten- und Kalenderdateien zum Importieren auf dem Gerät ablegen.</summary>
    public bool PlaceImportFiles
    {
        get => _placeImportFiles;
        set => SetProperty(ref _placeImportFiles, value);
    }

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

    public override Task ActivateAsync()
    {
        LoadSets();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------ Speicherort

    public AsyncRelayCommand BrowseSourceCommand { get; }

    public RelayCommand OpenSourceCommand { get; }

    /// <summary>
    /// Woher die Sätze kommen. Dieselbe Angabe wie beim Sichern und in den
    /// Einstellungen – ein anderer Ordner (etwa eine externe Platte) ist damit
    /// hier direkt wählbar.
    /// </summary>
    public string BackupRoot
    {
        get => _session.BackupRoot;
        set
        {
            if (_session.SetBackupRoot(value))
            {
                OnPropertyChanged();
                LoadSets();
            }
        }
    }

    private async Task BrowseSourceAsync()
    {
        var folder = await DialogService
            .PickFolderAsync(Tr("Ordner mit den Sicherungen", "Folder holding the backups"), BackupRoot)
            .ConfigureAwait(true);

        if (folder is null)
        {
            return;
        }

        BackupRoot = folder;
        StatusMessage = Sets.Count == 0
            ? Tr($"In {BackupRoot} liegt keine Sicherung.", $"No backup found in {BackupRoot}.")
            : Tr($"{Sets.Count} Sicherung(en) in {BackupRoot}.", $"{Sets.Count} backup(s) in {BackupRoot}.");
    }

    private void LoadSets()
    {
        var previous = SelectedSet?.Directory;
        Sets.Clear();
        foreach (var set in _session.Repository.LoadSets())
        {
            Sets.Add(set);
        }

        SelectedSet = Sets.FirstOrDefault(s => s.Directory == previous) ?? Sets.FirstOrDefault();
    }

    private void UpdateCategories()
    {
        Categories.Clear();
        if (SelectedSet is null)
        {
            return;
        }

        foreach (var group in SelectedSet.Manifest.Entries
                     .GroupBy(e => e.CategoryId)
                     .OrderBy(g => CategoryCatalog.IndexOf(g.Key)))
        {
            Categories.Add(new RestoreCategoryViewModel(group.Key, group.Count(), group.Sum(e => Math.Max(0, e.Size))));
        }
    }

    private void SetSelection(bool value)
    {
        foreach (var category in Categories)
        {
            category.IsSelected = value;
        }
    }

    private bool CanStart() =>
        !IsRunning && _session.HasDevice && _session.AdbAvailable && SelectedSet is not null;

    private async Task StartAsync()
    {
        if (!CanStart() || SelectedSet is null || _session.SelectedDevice is null)
        {
            return;
        }

        var categoryIds = Categories.Where(c => c.IsSelected).Select(c => c.CategoryId).ToList();
        if (categoryIds.Count == 0)
        {
            StatusMessage = Tr("Es ist keine Gruppe ausgewählt.", "No group is selected.");
            return;
        }

        var device = _session.DeviceInfo?.DisplayName ?? _session.SelectedDevice.DisplayName;
        var groups = string.Join(", ", categoryIds.Select(CategoryCatalog.DisplayNameOf));
        var confirmed = await DialogService.ConfirmAsync(
            Tr("Wiederherstellung starten", "Start restore"),
            Tr($"Der Sicherungssatz „{SelectedSet.Name}“ wird auf {device} zurückgespielt.",
               $"The backup set \"{SelectedSet.Name}\" will be restored to {device}.") +
            Environment.NewLine + Environment.NewLine +
            Tr($"Gruppen: {groups}", $"Groups: {groups}") +
            Environment.NewLine +
            Tr($"Konflikte: {SelectedConflictOption.DisplayName}", $"Conflicts: {SelectedConflictOption.DisplayName}") +
            (InstallApps
                ? Environment.NewLine + Tr("Apps werden installiert.", "Apps will be installed.")
                : string.Empty));

        if (!confirmed)
        {
            return;
        }

        IsRunning = true;
        _session.IsBusy = true;
        _session.BusyDescription = Tr("Wiederherstellung läuft", "Restore running");
        ResultText = string.Empty;
        Progress.Reset(Tr("Wiederherstellung wird vorbereitet", "Preparing restore"));
        _cancellation = new CancellationTokenSource();

        try
        {
            var options = new RestoreOptions
            {
                Set = SelectedSet,
                CategoryIds = categoryIds,
                ConflictMode = SelectedConflictOption.Mode,
                InstallApps = InstallApps,
                AllowDowngrade = AllowDowngrade,
                RestoreLegacyAppData = RestoreLegacyAppData,
                RestoreRootAppData = RestoreRootAppData,
                PlaceImportFiles = PlaceImportFiles
            };

            var progress = new Progress<OperationProgress>(p => Progress.Update(p));
            var service = _session.CreateRestoreService();
            var result = await service
                .RunAsync(_session.SelectedDevice.Serial, options, progress, _cancellation.Token)
                .ConfigureAwait(true);

            ResultText = result.Canceled ? Tr("Abgebrochen – ", "Cancelled – ") + result.SummaryText : result.SummaryText;
            StatusMessage = ResultText;

            var message = result.SummaryText +
                          (result.Warnings.Count == 0
                              ? string.Empty
                              : Environment.NewLine + Environment.NewLine + Tr("Hinweise:", "Notes:") + Environment.NewLine +
                                string.Join(Environment.NewLine, result.Warnings.Distinct().Take(15)));

            await DialogService.ShowInfoAsync(
                result.Canceled
                    ? Tr("Wiederherstellung abgebrochen", "Restore cancelled")
                    : Tr("Wiederherstellung abgeschlossen", "Restore finished"),
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

    private void RaiseCommandStates()
    {
        BrowseSourceCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    #region Beschriftungen

    public string LabelSourceFolder => Tr("Speicherort", "Location");

    public string LabelBrowse => Tr("Auswählen …", "Choose …");

    public string LabelOpenFolder => Tr("Öffnen", "Open");

    public string TipSourceFolder => Tr(
        "Ordner, in dem Pixel Backup nach Sicherungssätzen sucht – etwa eine externe Platte.",
        "Folder Pixel Backup looks in for backup sets – an external drive, for example.");

    public string PageHint => Tr(
        "Sicherungssatz wählen, Gruppen auswählen und auf das verbundene Gerät zurückspielen.",
        "Pick a backup set, choose the groups and restore them to the connected device.");

    public string SectionSets => Tr("Sicherungen", "Backups");

    public string SectionGroups => Tr("Diese Gruppen zurückspielen", "Restore these groups");

    public string LabelRefresh => Tr("Aktualisieren", "Refresh");

    public string LabelConflict => Tr("Wenn die Datei schon auf dem Gerät liegt:", "If the file already exists on the device:");

    public string LabelInstallApps => Tr("Apps installieren", "Install apps");

    public string LabelDowngrade => Tr("Ältere App-Version zulassen", "Allow older app version");

    public string LabelLegacyAppData => Tr("App-Daten (adb restore) zurückspielen", "Restore app data (adb restore)");

    public string LabelRootAppData => Tr("App-Daten aus Root-Sicherung zurückspielen", "Restore app data from the root backup");

    public string TipRootAppData => Tr(
        "Benötigt Root auf dem Zielgerät. Die Apps werden vorher installiert.",
        "Needs root on the target device. The apps are installed first.");

    public string LabelImportFiles => Tr("Importdateien auf dem Gerät ablegen", "Place import files on the device");

    public string TipImportFiles => Tr(
        "Legt kontakte.vcf, sms.xml und kalender.ics unter /sdcard/PixelBackup-Import ab.",
        "Places kontakte.vcf, sms.xml and kalender.ics under /sdcard/PixelBackup-Import.");

    public string LabelSelectAll => Tr("Alle", "All");

    public string LabelSelectNone => Tr("Keine", "None");

    public string LabelStart => Tr("Wiederherstellung starten", "Start restore");

    public string LabelCancel => Tr("Abbrechen", "Cancel");

    #endregion

    private void ReportError(Exception ex)
    {
        _session.Log.Error(Tr("Wiederherstellung fehlgeschlagen", "Restore failed"), ex);
        StatusMessage = Tr("Fehler: ", "Error: ") + ex.Message;
        _ = DialogService.ShowInfoAsync(Tr("Fehler", "Error"), ex.Message);
    }
}
