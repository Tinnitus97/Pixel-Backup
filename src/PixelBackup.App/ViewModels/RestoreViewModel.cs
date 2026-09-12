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

    public string SummaryText => $"{Humanize.Count(ItemCount, "Element", "Elemente")} · {Humanize.Bytes(Bytes)}";

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
        Title = "Wiederherstellen";
        Icon = "♻";

        ConflictOptions = new ObservableCollection<ConflictOption>
        {
            new(ConflictMode.Skip, "Vorhandene Dateien behalten"),
            new(ConflictMode.Overwrite, "Vorhandene Dateien überschreiben"),
            new(ConflictMode.KeepBoth, "Beide behalten (Namenszusatz)")
        };
        _conflictOption = ConflictOptions[0];

        RefreshCommand = new RelayCommand(LoadSets);
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
        ? "Bitte links einen Sicherungssatz auswählen."
        : $"{SelectedSet.DeviceName} · erstellt am {SelectedSet.CreatedText} · zuletzt aktualisiert {SelectedSet.UpdatedText}" +
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
                     .OrderBy(g => CategoryCatalog.IndexOf(CategoryCatalog.ById(g.Key) ?? new BackupCategory
                     {
                         Id = g.Key,
                         DisplayName = g.Key,
                         Description = string.Empty
                     })))
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
            StatusMessage = "Es ist keine Gruppe ausgewählt.";
            return;
        }

        var device = _session.DeviceInfo?.DisplayName ?? _session.SelectedDevice.DisplayName;
        var confirmed = await DialogService.ConfirmAsync(
            "Wiederherstellung starten",
            $"Der Sicherungssatz „{SelectedSet.Name}“ wird auf {device} zurückgespielt." +
            Environment.NewLine + Environment.NewLine +
            $"Gruppen: {string.Join(", ", categoryIds.Select(CategoryCatalog.DisplayNameOf))}" +
            Environment.NewLine +
            $"Konflikte: {SelectedConflictOption.DisplayName}" +
            (InstallApps ? Environment.NewLine + "Apps werden installiert." : string.Empty));

        if (!confirmed)
        {
            return;
        }

        IsRunning = true;
        _session.IsBusy = true;
        _session.BusyDescription = "Wiederherstellung läuft";
        ResultText = string.Empty;
        Progress.Reset("Wiederherstellung wird vorbereitet");
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

            ResultText = result.Canceled ? "Abgebrochen – " + result.SummaryText : result.SummaryText;
            StatusMessage = ResultText;

            var message = result.SummaryText +
                          (result.Warnings.Count == 0
                              ? string.Empty
                              : Environment.NewLine + Environment.NewLine + "Hinweise:" + Environment.NewLine +
                                string.Join(Environment.NewLine, result.Warnings.Distinct().Take(15)));

            await DialogService.ShowInfoAsync(
                result.Canceled ? "Wiederherstellung abgebrochen" : "Wiederherstellung abgeschlossen",
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
        StartCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error("Wiederherstellung fehlgeschlagen", ex);
        StatusMessage = "Fehler: " + ex.Message;
        _ = DialogService.ShowInfoAsync("Fehler", ex.Message);
    }
}
