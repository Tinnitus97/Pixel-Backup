using System.Collections.ObjectModel;
using PixelBackup.App.Mvvm;
using PixelBackup.App.Services;
using PixelBackup.Core.Adb;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Diagnostics;
using PixelBackup.Core.Services;

namespace PixelBackup.App.ViewModels;

/// <summary>Rahmen der Anwendung: Navigation, Kopfzeile und Startlogik.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    private readonly AppSession _session;
    private ViewModelBase _selectedPage;
    private int _languageIndex;

    public MainWindowViewModel(AppSession session)
    {
        _session = session;

        Devices = new DevicesViewModel(session);
        Backup = new BackupViewModel(session);
        Restore = new RestoreViewModel(session);
        Library = new LibraryViewModel(session);
        Log = new LogViewModel(session);
        Components = new ComponentsViewModel(session);
        Settings = new SettingsViewModel(session);

        Pages = new ObservableCollection<ViewModelBase>
        {
            Devices, Backup, Restore, Library, Log, Components, Settings
        };
        _selectedPage = Devices;

        RefreshDevicesCommand = new AsyncRelayCommand(
            () => _session.RefreshDevicesAsync(),
            () => _session.AdbAvailable);

        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        _languageIndex = Localizer.I.Lang == AppLanguage.En ? 1 : 0;

        _session.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(AppSession.SelectedDevice):
                case nameof(AppSession.DeviceInfo):
                    OnPropertiesChanged(nameof(DeviceHeadline), nameof(DeviceSubline), nameof(HasDevice));
                    break;
                case nameof(AppSession.AdbAvailable):
                case nameof(AppSession.AdbStatus):
                    OnPropertiesChanged(nameof(AdbStatus), nameof(AdbAvailable));
                    RefreshDevicesCommand.RaiseCanExecuteChanged();
                    break;
                case nameof(AppSession.IsBusy):
                case nameof(AppSession.BusyDescription):
                    OnPropertiesChanged(nameof(IsBusy), nameof(BusyDescription));
                    break;
            }
        };

        _session.DeviceConnected += OnDeviceConnected;
        Localizer.I.LanguageChanged += () =>
        {
            _languageIndex = Localizer.I.Lang == AppLanguage.En ? 1 : 0;
            OnPropertyChanged(string.Empty);
        };
    }

    public AppSession Session => _session;

    public ObservableCollection<ViewModelBase> Pages { get; }

    public DevicesViewModel Devices { get; }

    public BackupViewModel Backup { get; }

    public RestoreViewModel Restore { get; }

    public LibraryViewModel Library { get; }

    public LogViewModel Log { get; }

    public ComponentsViewModel Components { get; }

    public SettingsViewModel Settings { get; }

    public AsyncRelayCommand RefreshDevicesCommand { get; }

    // --------------------------------------------- Sprache und Erscheinungsbild

    /// <summary>Beide Sprachen für die Auswahl in der Kopfzeile.</summary>
    public IReadOnlyList<string> Languages { get; } = new[] { "Deutsch", "English" };

    /// <summary>Umschalten zwischen hell und dunkel – ohne Umweg über die Einstellungen.</summary>
    public RelayCommand ToggleThemeCommand { get; }

    /// <summary>Sonne bei dunklem Bild (dann wird es hell), sonst Mond.</summary>
    public string ThemeIcon => ThemeService.IsDark ? "\u2600" : "\u263D";

    public string ThemeTooltip => Loc.Tr("Hell oder dunkel umschalten", "Switch light or dark");

    public string LanguageTooltip => Loc.Tr("Sprache der Oberfläche", "Interface language");

    public int SelectedLanguageIndex
    {
        get => _languageIndex;
        set
        {
            if (!SetProperty(ref _languageIndex, value))
            {
                return;
            }

            // Feste Wahl: sie soll auch nach einem Neustart gelten.
            _session.Settings.Language = value == 1 ? LanguageSetting.English : LanguageSetting.German;
            _session.ApplyLanguage();
            _session.SaveSettings();
        }
    }

    private void ToggleTheme()
    {
        var dark = ThemeService.IsDark;
        _session.Settings.Theme = dark ? AppTheme.Light : AppTheme.Dark;
        _session.ApplyTheme();
        _session.SaveSettings();

        OnPropertiesChanged(nameof(ThemeIcon));
        Settings.RefreshTexts();
    }

    public ViewModelBase SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value) && value is not null)
            {
                _ = value.ActivateAsync();
            }
        }
    }

    public string WindowTitle => Loc.Tr(
        "Pixel Backup – Sicherung für Android-Geräte",
        "Pixel Backup – backup for Android devices");

    public string Header => "Pixel Backup";

    public string SubHeader => Loc.Tr(
        "Vollsicherung und Wiederherstellung für Android-Geräte – direkt über adb",
        "Full backup and restore for Android devices – straight through adb");

    public string LabelFindDevices => Loc.Tr("Geräte suchen", "Find devices");

    public bool AdbAvailable => _session.AdbAvailable;

    public string AdbStatus => _session.AdbStatus;

    public bool HasDevice => _session.HasDevice;

    public bool IsBusy => _session.IsBusy;

    public string BusyDescription => _session.BusyDescription;

    public string DeviceHeadline => _session.SelectedDevice is null
        ? Loc.Tr("Kein Gerät verbunden", "No device connected")
        : _session.DeviceInfo?.DisplayName ?? _session.SelectedDevice.DisplayName;

    public string DeviceSubline
    {
        get
        {
            if (_session.SelectedDevice is null)
            {
                return Loc.Tr(
                    "Bitte ein Android-Gerät per USB anschließen und USB-Debugging bestätigen.",
                    "Please connect an Android device by USB and confirm USB debugging.");
            }

            var info = _session.DeviceInfo;
            return info is null
                ? _session.SelectedDevice.StateText
                : $"{info.AndroidText} · {info.BatteryText} · {info.StorageText}";
        }
    }

    public async Task InitializeAsync()
    {
        await _session.InitializeAsync().ConfigureAwait(true);
        await SelectedPage.ActivateAsync().ConfigureAwait(true);
    }

    private void OnDeviceConnected(AdbDevice device)
    {
        if (!_session.Settings.AutoBackupOnConnect || _session.IsBusy)
        {
            return;
        }

        _session.Log.Info(Loc.Tr(
            $"Automatische Sicherung für {device.DisplayName} wird vorbereitet.",
            $"Preparing the automatic backup for {device.DisplayName}."));
        SelectedPage = Backup;
        _ = Backup.RunAutomaticBackupAsync();
    }
}
