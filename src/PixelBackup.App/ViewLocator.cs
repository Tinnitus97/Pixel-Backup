using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PixelBackup.App.ViewModels;

namespace PixelBackup.App;

/// <summary>Ordnet jedem Ansichtsmodell die gleichnamige Ansicht zu (…ViewModel → …View).</summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is null)
        {
            return new TextBlock { Text = "Ansicht nicht gefunden: " + name };
        }

        return (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is ViewModelBase;
}
