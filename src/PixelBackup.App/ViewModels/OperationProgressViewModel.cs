using PixelBackup.App.Mvvm;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Model;

namespace PixelBackup.App.ViewModels;

/// <summary>Zeigt den Fortschritt eines laufenden Vorgangs an.</summary>
public sealed class OperationProgressViewModel : ObservableObject
{
    private double _percent;
    private double _currentPercent;
    private bool _currentIsIndeterminate = true;
    private string _currentBytesText = "–";
    private string _phase = string.Empty;
    private string _currentItem = string.Empty;
    private string _countText = "0 / 0";
    private string _bytesText = "–";
    private string _speedText = "–";
    private string _etaText = "–";
    private bool _isIndeterminate;

    /// <summary>Gesamtfortschritt des Laufes – der untere Balken.</summary>
    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    /// <summary>Fortschritt der laufenden Gruppe – der obere Balken.</summary>
    public double CurrentPercent
    {
        get => _currentPercent;
        private set => SetProperty(ref _currentPercent, value);
    }

    /// <summary>
    /// Lässt sich der laufende Schritt nicht beziffern (Vorbereitung,
    /// Archivierung), läuft der obere Balken durch, statt eine Zahl zu erfinden.
    /// </summary>
    public bool CurrentIsIndeterminate
    {
        get => _currentIsIndeterminate;
        private set => SetProperty(ref _currentIsIndeterminate, value);
    }

    public string CurrentBytesText
    {
        get => _currentBytesText;
        private set => SetProperty(ref _currentBytesText, value);
    }

    /// <summary>Beschriftung des oberen Balkens: die laufende Gruppe.</summary>
    public string LabelCurrent => Loc.Tr("Aktuell", "Current");

    /// <summary>Beschriftung des unteren Balkens.</summary>
    public string LabelTotal => Loc.Tr("Gesamt", "Total");

    public string CurrentPercentText => CurrentIsIndeterminate ? "…" : $"{CurrentPercent:F0} %";

    public string PercentText => IsIndeterminate ? "…" : $"{Percent:F0} %";

    public string Phase
    {
        get => _phase;
        private set => SetProperty(ref _phase, value);
    }

    public string CurrentItem
    {
        get => _currentItem;
        private set => SetProperty(ref _currentItem, value);
    }

    public string CountText
    {
        get => _countText;
        private set => SetProperty(ref _countText, value);
    }

    public string BytesText
    {
        get => _bytesText;
        private set => SetProperty(ref _bytesText, value);
    }

    public string SpeedText
    {
        get => _speedText;
        private set => SetProperty(ref _speedText, value);
    }

    public string EtaText
    {
        get => _etaText;
        private set => SetProperty(ref _etaText, value);
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    public void Update(OperationProgress progress)
    {
        Percent = progress.Percent;
        CurrentPercent = progress.CurrentPercent;
        CurrentIsIndeterminate = !progress.HasCurrentProgress;
        CurrentBytesText = progress.CurrentBytesText;
        Phase = progress.Phase;
        CurrentItem = progress.CurrentItem;
        CountText = progress.CountText;
        BytesText = progress.BytesText;
        SpeedText = progress.SpeedText;
        EtaText = progress.EtaText;
        IsIndeterminate = progress.BytesTotal <= 0 && progress.ItemsTotal <= 0;

        OnPropertiesChanged(nameof(CurrentPercentText), nameof(PercentText));
    }

    public void Reset(string phase = "")
    {
        Percent = 0;
        CurrentPercent = 0;
        CurrentIsIndeterminate = true;
        CurrentBytesText = "–";
        Phase = phase;
        CurrentItem = string.Empty;
        CountText = "0 / 0";
        BytesText = "–";
        SpeedText = "–";
        EtaText = "–";
        IsIndeterminate = false;

        OnPropertiesChanged(nameof(CurrentPercentText), nameof(PercentText));
    }
}
