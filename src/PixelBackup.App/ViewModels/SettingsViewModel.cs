using System.Collections.ObjectModel;
using Avalonia.Styling;
using Avalonia;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Services;

namespace PixelBackup.App.ViewModels;

public sealed record ThemeOption(AppTheme Theme, string DisplayName);

/// <summary>Seite "Einstellungen".</summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly AppSession _session;
    private ThemeOption _selectedTheme;

    public SettingsViewModel(AppSession session)
    {
        _session = session;
        Title = "Einstellungen";
        Icon = "⚙";

        ThemeOptions = new ObservableCollection<ThemeOption>
        {
            new(AppTheme.System, "Wie das System"),
            new(AppTheme.Light, "Hell"),
            new(AppTheme.Dark, "Dunkel")
        };
        _selectedTheme = ThemeOptions.First(t => t.Theme == session.Settings.Theme);

        BrowseBackupRootCommand = new AsyncRelayCommand(BrowseBackupRootAsync, null, ReportError);
        BrowseAdbCommand = new AsyncRelayCommand(BrowseAdbAsync, null, ReportError);
        DetectAdbCommand = new AsyncRelayCommand(DetectAdbAsync, null, ReportError);
        OpenBackupFolderCommand = new RelayCommand(() => DialogService.OpenInFileManager(BackupRoot));
        OpenSettingsFolderCommand = new RelayCommand(() => DialogService.OpenInFileManager(
            Path.GetDirectoryName(_session.SettingsFilePath) ?? _session.SettingsFilePath));
        SaveCommand = new RelayCommand(Save);

        ApplyTheme(session.Settings.Theme);
    }

    public AppSession Session => _session;

    public ObservableCollection<ThemeOption> ThemeOptions { get; }

    public AsyncRelayCommand BrowseBackupRootCommand { get; }

    public AsyncRelayCommand BrowseAdbCommand { get; }

    public AsyncRelayCommand DetectAdbCommand { get; }

    public RelayCommand OpenBackupFolderCommand { get; }

    public RelayCommand OpenSettingsFolderCommand { get; }

    public RelayCommand SaveCommand { get; }

    public string BackupRoot
    {
        get => _session.Settings.BackupRoot;
        set
        {
            if (_session.Settings.BackupRoot == value)
            {
                return;
            }

            _session.Settings.BackupRoot = value;
            _session.Repository.Root = value;
            OnPropertyChanged();
        }
    }

    public string AdbPath
    {
        get => _session.Settings.AdbPath ?? string.Empty;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_session.Settings.AdbPath == normalized)
            {
                return;
            }

            _session.Settings.AdbPath = normalized;
            OnPropertyChanged();
        }
    }

    public bool ComputeHashes
    {
        get => _session.Settings.ComputeHashes;
        set
        {
            _session.Settings.ComputeHashes = value;
            OnPropertyChanged();
        }
    }

    public bool IncrementalByDefault
    {
        get => _session.Settings.IncrementalByDefault;
        set
        {
            _session.Settings.IncrementalByDefault = value;
            OnPropertyChanged();
        }
    }

    public bool RemoveDeletedFiles
    {
        get => _session.Settings.RemoveDeletedFiles;
        set
        {
            _session.Settings.RemoveDeletedFiles = value;
            OnPropertyChanged();
        }
    }

    public bool CreateArchive
    {
        get => _session.Settings.CreateArchive;
        set
        {
            _session.Settings.CreateArchive = value;
            OnPropertyChanged();
        }
    }

    public bool AutoBackupOnConnect
    {
        get => _session.Settings.AutoBackupOnConnect;
        set
        {
            _session.Settings.AutoBackupOnConnect = value;
            OnPropertyChanged();
        }
    }

    public bool WatchDevices
    {
        get => _session.Settings.WatchDevices;
        set
        {
            _session.Settings.WatchDevices = value;
            OnPropertyChanged();
            if (!value)
            {
                _session.StopWatcher();
            }
        }
    }

    public int KeepSetsPerDevice
    {
        get => _session.Settings.KeepSetsPerDevice;
        set
        {
            _session.Settings.KeepSetsPerDevice = Math.Max(0, value);
            OnPropertyChanged();
        }
    }

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                _session.Settings.Theme = value.Theme;
                ApplyTheme(value.Theme);
            }
        }
    }

    public string AdbStatusText => _session.AdbStatus;

    private async Task BrowseBackupRootAsync()
    {
        var folder = await DialogService.PickFolderAsync("Ordner für die Sicherungen", BackupRoot);
        if (folder is not null)
        {
            BackupRoot = folder;
            Save();
        }
    }

    private async Task BrowseAdbAsync()
    {
        var file = await DialogService.PickFileAsync("adb auswählen", Path.GetDirectoryName(AdbPath));
        if (file is not null)
        {
            AdbPath = file;
            Save();
            await _session.ConnectAdbAsync().ConfigureAwait(true);
            OnPropertyChanged(nameof(AdbStatusText));
        }
    }

    private async Task DetectAdbAsync()
    {
        var found = AdbLocator.Locate(_session.Settings.AdbPath);
        StatusMessage = found is null
            ? "adb wurde nicht gefunden. Bitte die Android-Plattform-Tools installieren."
            : "adb gefunden: " + found;

        await _session.ConnectAdbAsync().ConfigureAwait(true);
        OnPropertyChanged(nameof(AdbStatusText));
    }

    private void Save()
    {
        _session.SaveSettings();
        StatusMessage = "Einstellungen gespeichert.";
    }

    private static void ApplyTheme(AppTheme theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme switch
        {
            AppTheme.Light => ThemeVariant.Light,
            AppTheme.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void ReportError(Exception ex)
    {
        _session.Log.Error("Einstellung konnte nicht übernommen werden", ex);
        StatusMessage = "Fehler: " + ex.Message;
    }
}
