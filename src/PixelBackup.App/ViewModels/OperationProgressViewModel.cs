using PixelBackup.App.Mvvm;
using PixelBackup.Core.Model;

namespace PixelBackup.App.ViewModels;

/// <summary>Zeigt den Fortschritt eines laufenden Vorgangs an.</summary>
public sealed class OperationProgressViewModel : ObservableObject
{
    private double _percent;
    private string _phase = string.Empty;
    private string _currentItem = string.Empty;
    private string _countText = "0 / 0";
    private string _bytesText = "–";
    private string _speedText = "–";
    private string _etaText = "–";
    private bool _isIndeterminate;

    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

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
        Phase = progress.Phase;
        CurrentItem = progress.CurrentItem;
        CountText = progress.CountText;
        BytesText = progress.BytesText;
        SpeedText = progress.SpeedText;
        EtaText = progress.EtaText;
        IsIndeterminate = progress.BytesTotal <= 0 && progress.ItemsTotal <= 0;
    }

    public void Reset(string phase = "")
    {
        Percent = 0;
        Phase = phase;
        CurrentItem = string.Empty;
        CountText = "0 / 0";
        BytesText = "–";
        SpeedText = "–";
        EtaText = "–";
        IsIndeterminate = false;
    }
}
