using Avalonia.Controls;
using PixelBackup.App.Services;

namespace PixelBackup.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Dialoge benötigen ein Elternfenster.
        DialogService.MainWindow = this;
    }
}
