using Avalonia;
using Avalonia.Styling;
using PixelBackup.Core.Localization;
using PixelBackup.Core.Services;

namespace PixelBackup.App.Services;

/// <summary>
/// Setzt das Erscheinungsbild. Bei "Wie das System" übernimmt Avalonia die
/// Einstellung des Betriebssystems (hell/dunkel) selbsttätig.
/// </summary>
public static class ThemeService
{
    public static void Apply(AppTheme theme)
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

    /// <summary>Das gerade tatsächlich verwendete Erscheinungsbild.</summary>
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>"Wie das System (erkannt: Dunkel)" für die Einstellungsseite.</summary>
    public static string DescribeDetected() =>
        IsDark ? Loc.Tr("erkannt: Dunkel", "detected: dark") : Loc.Tr("erkannt: Hell", "detected: light");
}
