using PixelBackup.App.Mvvm;

namespace PixelBackup.App.ViewModels;

/// <summary>Basis aller Seiten-Ansichtsmodelle; liefert Titel und Symbol für die Navigation.</summary>
public abstract class ViewModelBase : ObservableObject
{
    private string _title = string.Empty;
    private string _icon = "•";
    private string _statusMessage = string.Empty;

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

    /// <summary>Wird aufgerufen, sobald die Seite angezeigt wird.</summary>
    public virtual Task ActivateAsync() => Task.CompletedTask;
}
