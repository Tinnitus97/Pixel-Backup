using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Model;

public enum ComponentState
{
    /// <summary>Noch nicht geprüft.</summary>
    Unknown,

    /// <summary>Nicht vorhanden – muss installiert werden.</summary>
    Missing,

    /// <summary>Vorhanden und aktuell.</summary>
    UpToDate,

    /// <summary>Vorhanden, es gibt aber eine neuere Fassung.</summary>
    UpdateAvailable,

    /// <summary>Vorhanden, aber es gibt ein Problem (z. B. Gerät ohne Treiber).</summary>
    Problem,

    /// <summary>Auf diesem Betriebssystem nicht erforderlich.</summary>
    NotRequired
}

/// <summary>Zustand einer benötigten Komponente (adb, USB-Treiber …).</summary>
public sealed class ComponentStatus
{
    public required string Name { get; init; }

    public ComponentState State { get; set; } = ComponentState.Unknown;

    public string? InstalledVersion { get; set; }

    public string? LatestVersion { get; set; }

    public string? Location { get; set; }

    /// <summary>Erklärender Text in der eingestellten Sprache.</summary>
    public string Message { get; set; } = string.Empty;

    public bool CanInstall => State is ComponentState.Missing or ComponentState.Problem;

    public bool CanUpdate => State == ComponentState.UpdateAvailable;

    public bool IsHealthy => State is ComponentState.UpToDate or ComponentState.NotRequired;

    /// <summary>
    /// Gibt es hier etwas einzurichten? Alles außer „aktuell“ und „nicht
    /// erforderlich“ zählt dazu – auch „nicht geprüft“, damit sich die
    /// Komponente ohne Netzverbindung von Hand einrichten lässt.
    /// </summary>
    public bool NeedsSetup => !IsHealthy;

    public string Glyph => State switch
    {
        ComponentState.UpToDate => "✔",
        ComponentState.NotRequired => "–",
        ComponentState.UpdateAvailable => "⬆",
        ComponentState.Missing => "✖",
        ComponentState.Problem => "!",
        _ => "?"
    };

    public string StateText => State switch
    {
        ComponentState.UpToDate => Loc.Tr("aktuell", "up to date"),
        ComponentState.UpdateAvailable => Loc.Tr("Update verfügbar", "update available"),
        ComponentState.Missing => Loc.Tr("fehlt", "missing"),
        ComponentState.Problem => Loc.Tr("Problem erkannt", "problem detected"),
        ComponentState.NotRequired => Loc.Tr("nicht erforderlich", "not required"),
        _ => Loc.Tr("nicht geprüft", "not checked")
    };

    public string VersionText
    {
        get
        {
            if (string.IsNullOrEmpty(InstalledVersion))
            {
                return string.IsNullOrEmpty(LatestVersion)
                    ? "–"
                    : Loc.Tr($"verfügbar: {LatestVersion}", $"available: {LatestVersion}");
            }

            return string.IsNullOrEmpty(LatestVersion) || LatestVersion == InstalledVersion
                ? InstalledVersion!
                : $"{InstalledVersion} → {LatestVersion}";
        }
    }
}
