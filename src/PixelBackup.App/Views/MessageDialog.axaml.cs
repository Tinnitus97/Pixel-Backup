using Avalonia.Controls;
using Avalonia.Interactivity;
using PixelBackup.Core.Localization;

namespace PixelBackup.App.Views;

/// <summary>Inhalt eines Hinweis- oder Bestätigungsfensters.</summary>
public sealed class MessageDialogContent
{
    public required string Message { get; init; }

    public bool IsConfirm { get; init; }

    public string OkText => IsConfirm ? Loc.Tr("Fortfahren", "Continue") : "OK";

    public string CancelText => Loc.Tr("Abbrechen", "Cancel");
}

public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>Zeigt einen Hinweis oder eine Rückfrage an. Ohne Elternfenster wird "nein" geliefert.</summary>
    public static async Task<bool> ShowAsync(Window? owner, string title, string message, bool confirm)
    {
        if (owner is null)
        {
            return false;
        }

        var dialog = new MessageDialog
        {
            Title = title,
            DataContext = new MessageDialogContent { Message = message, IsConfirm = confirm }
        };

        return await dialog.ShowDialog<bool>(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
