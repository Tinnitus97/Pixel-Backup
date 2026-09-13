using PixelBackup.App.Mvvm;
using PixelBackup.Core.Localization;

namespace PixelBackup.App.ViewModels;

/// <summary>
/// Basis aller Seiten-Ansichtsmodelle; liefert Titel und Symbol für die Navigation
/// und frischt bei einem Sprachwechsel sämtliche Beschriftungen auf.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    private string _title = string.Empty;
    private string _icon = "•";
    private string _statusMessage = string.Empty;

    protected ViewModelBase()
    {
        Localizer.I.LanguageChanged += OnLanguageChanged;
    }

    public string Title
    {
        get => _title;
        protected set => SetProperty(ref _title, value);
    }

    public string Icon
    {
        get => _icon;
        protected set => SetProperty(ref _icon, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Kurzform für Loc.Tr – in den Ansichtsmodellen ständig gebraucht.</summary>
    protected static string Tr(string de, string en) => Loc.Tr(de, en);

    /// <summary>Setzt Titel und Symbol in der aktuellen Sprache. In den Seiten überschrieben.</summary>
    protected virtual void UpdateTitle()
    {
    }

    /// <summary>Meldet alle Beschriftungen als geändert (nach einem Sprachwechsel).</summary>
    public virtual void RefreshTexts()
    {
        UpdateTitle();
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged()
    {
        // Die letzte Meldung stammt aus der alten Sprache – lieber nichts als halb übersetzt.
        StatusMessage = string.Empty;
        RefreshTexts();
    }

    /// <summary>Wird aufgerufen, sobald die Seite angezeigt wird.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}
