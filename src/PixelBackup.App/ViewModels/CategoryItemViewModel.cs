using PixelBackup.App.Mvvm;
using PixelBackup.Core.Model;
using PixelBackup.Core.Util;

namespace PixelBackup.App.ViewModels;

/// <summary>Eine auswählbare Sicherungsgruppe in der Liste.</summary>
public sealed class CategoryItemViewModel : ObservableObject
{
    private bool _isSelected;
    private int _itemCount;
    private long _bytes;
    private bool _analyzed;
    private bool _isAvailable = true;
    private string _unavailableReason = string.Empty;

    public CategoryItemViewModel(BackupCategory category, bool isSelected)
    {
        Category = category;
        _isSelected = isSelected;
    }

    public BackupCategory Category { get; }

    public string Id => Category.Id;

    public string DisplayName => Category.DisplayName;

    public string Description => Category.Description;

    public string Icon => Category.Icon;

    public string? Caveat => Category.Caveat;

    public bool HasCaveat => !string.IsNullOrWhiteSpace(Category.Caveat);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                SelectionChanged?.Invoke();
            }
        }
    }

    /// <summary>Falsch, wenn die Gruppe auf diesem Gerät nicht möglich ist (z. B. ohne Root).</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(NoteText));
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    public string UnavailableReason
    {
        get => _unavailableReason;
        private set
        {
            if (SetProperty(ref _unavailableReason, value))
            {
                OnPropertyChanged(nameof(NoteText));
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    /// <summary>Hinweiszeile: entweder der Grund für die Sperre oder der Kategoriehinweis.</summary>
    public string NoteText => IsAvailable ? Caveat ?? string.Empty : UnavailableReason;

    public bool HasNote => !string.IsNullOrWhiteSpace(NoteText);

    public void SetAvailability(bool available, string reason)
    {
        UnavailableReason = reason;
        IsAvailable = available;

        if (!available && IsSelected)
        {
            IsSelected = false;
        }
    }

    public event Action? SelectionChanged;

    public int ItemCount
    {
        get => _itemCount;
        private set
        {
            if (SetProperty(ref _itemCount, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public long Bytes
    {
        get => _bytes;
        private set
        {
            if (SetProperty(ref _bytes, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool Analyzed
    {
        get => _analyzed;
        private set
        {
            if (SetProperty(ref _analyzed, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public string SummaryText => Analyzed
        ? ItemCount == 0
            ? "nichts gefunden"
            : $"{Humanize.Count(ItemCount, "Element", "Elemente")} · {Humanize.Bytes(Bytes)}"
        : "noch nicht analysiert";

    public void ApplyAnalysis(int itemCount, long bytes)
    {
        ItemCount = itemCount;
        Bytes = bytes;
        Analyzed = true;
    }

    public void ResetAnalysis()
    {
        ItemCount = 0;
        Bytes = 0;
        Analyzed = false;
    }
}
