namespace PixelBackup.Core.Model;

/// <summary>Einstellungen für einen Sicherungslauf.</summary>
public sealed class BackupOptions
{
    public required string BackupRoot { get; init; }

    /// <summary>Aktualisiert einen bestehenden Satz, statt einen neuen anzulegen.</summary>
    public bool Incremental { get; init; }

    /// <summary>Der zu aktualisierende Satz – nur bei <see cref="Incremental"/>.</summary>
    public BackupSet? TargetSet { get; init; }

    /// <summary>SHA-256-Prüfsummen mitschreiben (ermöglicht später eine echte Überprüfung).</summary>
    public bool ComputeHashes { get; init; } = true;

    /// <summary>Am Ende ein ZIP-Archiv des Satzes erstellen.</summary>
    public bool CreateArchive { get; init; }

    /// <summary>Kennwort für das Archiv; leer bedeutet unverschlüsselt.</summary>
    public string? ArchivePassword { get; init; }

    /// <summary>Beim inkrementellen Lauf lokale Kopien löschen, die es am Gerät nicht mehr gibt.</summary>
    public bool RemoveDeletedFiles { get; init; }

    /// <summary>Bei der klassischen App-Daten-Sicherung auch die APKs einschließen.</summary>
    public bool LegacyBackupWithApks { get; init; } = true;

    public string? Notes { get; init; }
}

public enum ConflictMode
{
    /// <summary>Vorhandene Dateien am Gerät bleiben unangetastet.</summary>
    Skip,

    /// <summary>Vorhandene Dateien werden überschrieben.</summary>
    Overwrite,

    /// <summary>Die wiederhergestellte Datei erhält einen Namenszusatz.</summary>
    KeepBoth
}

/// <summary>Einstellungen für eine Wiederherstellung.</summary>
public sealed class RestoreOptions
{
    public required BackupSet Set { get; init; }

    public required IReadOnlyList<string> CategoryIds { get; init; }

    public ConflictMode ConflictMode { get; init; } = ConflictMode.Skip;

    public bool InstallApps { get; init; } = true;

    public bool AllowDowngrade { get; init; }

    public bool RestoreLegacyAppData { get; init; }

    /// <summary>Löst nach dem Kopieren den Medienscanner aus, damit Fotos sofort erscheinen.</summary>
    public bool TriggerMediaScan { get; init; } = true;
}
