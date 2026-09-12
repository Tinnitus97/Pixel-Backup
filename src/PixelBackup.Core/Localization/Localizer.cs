using System.Globalization;

namespace PixelBackup.Core.Localization;

public enum AppLanguage
{
    De,
    En
}

/// <summary>
/// Zentrale Sprachverwaltung. Hält die aktuelle Sprache und liefert über
/// Tr(de, en) den passenden Text. Änderungen melden sich per Ereignis, damit
/// die Oberfläche ihre Beschriftungen auffrischen kann.
/// </summary>
public sealed class Localizer
{
    public static Localizer I { get; } = new();

    private AppLanguage _lang = AppLanguage.De;

    public AppLanguage Lang
    {
        get => _lang;
        set
        {
            if (_lang == value)
            {
                return;
            }

            _lang = value;
            ApplyCulture();
            LanguageChanged?.Invoke();
        }
    }

    /// <summary>Wird bei jedem Sprachwechsel ausgelöst.</summary>
    public event Action? LanguageChanged;

    /// <summary>Wählt je nach aktueller Sprache den deutschen oder englischen Text.</summary>
    public string Tr(string de, string en) => _lang == AppLanguage.En ? en : de;

    public string CultureName => _lang == AppLanguage.En ? "en-US" : "de-DE";

    /// <summary>
    /// Ermittelt die Anzeigesprache des Systems. Deutsch bleibt Deutsch,
    /// alles andere wird auf Englisch abgebildet.
    /// </summary>
    public static AppLanguage DetectFromSystem()
    {
        try
        {
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName?.ToLowerInvariant();
            return two == "de" ? AppLanguage.De : AppLanguage.En;
        }
        catch (Exception)
        {
            return AppLanguage.De;
        }
    }

    /// <summary>Stellt Zahlen- und Datumsformate auf die gewählte Sprache um.</summary>
    public void ApplyCulture()
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(CultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Ohne die passenden Kulturdaten bleibt die Formatierung, wie sie ist.
        }
    }
}

/// <summary>Bequemer statischer Zugriff: Loc.Tr("…", "…").</summary>
public static class Loc
{
    public static string Tr(string de, string en) => Localizer.I.Tr(de, en);

    public static AppLanguage Lang => Localizer.I.Lang;

    /// <summary>Deutsch oder Englisch – je nach Sprache, für Aufzählungen ohne Übersetzung.</summary>
    public static string Plural(int count, string singularDe, string pluralDe, string singularEn, string pluralEn) =>
        Localizer.I.Lang == AppLanguage.En
            ? (count == 1 ? singularEn : pluralEn)
            : (count == 1 ? singularDe : pluralDe);
}
