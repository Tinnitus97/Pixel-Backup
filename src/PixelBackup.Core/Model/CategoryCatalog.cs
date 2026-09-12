namespace PixelBackup.Core.Model;

/// <summary>Der fest eingebaute Katalog aller Sicherungsgruppen.</summary>
public static class CategoryCatalog
{
    public const string StorageRoot = "/sdcard";

    public static IReadOnlyList<BackupCategory> All { get; } = new List<BackupCategory>
    {
        new()
        {
            Id = "photos",
            DisplayName = "Fotos",
            Description = "Kamerabilder, Screenshots und alle weiteren Bilddateien aus DCIM und Pictures.",
            Icon = "🖼",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/DCIM", "/sdcard/Pictures", "/sdcard/Camera", "/sdcard/Screenshots" },
            Extensions = new[] { "jpg", "jpeg", "png", "heic", "heif", "webp", "gif", "bmp", "dng", "raw", "tif", "tiff", "avif", "jfif" }
        },
        new()
        {
            Id = "videos",
            DisplayName = "Videos",
            Description = "Videoaufnahmen aus DCIM, Movies und Pictures.",
            Icon = "🎬",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[]
            {
                "/sdcard/DCIM", "/sdcard/Movies", "/sdcard/Pictures", "/sdcard/Video",
                "/sdcard/Videos", "/sdcard/Camera"
            },
            Extensions = new[] { "mp4", "mkv", "3gp", "mov", "webm", "avi", "m4v", "ts", "mts", "flv", "wmv" }
        },
        new()
        {
            Id = "audio",
            DisplayName = "Musik & Aufnahmen",
            Description = "Musikdateien, Sprachaufnahmen, Hörbücher und Podcasts.",
            Icon = "🎵",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[]
            {
                "/sdcard/Music", "/sdcard/Audio", "/sdcard/Recordings", "/sdcard/Podcasts",
                "/sdcard/Audiobooks",
                // herstellereigene Ordner: Samsung, Xiaomi/MIUI, Sony, LG
                "/sdcard/Sounds", "/sdcard/MIUI/sound_recorder", "/sdcard/Voice Recorder",
                "/sdcard/VoiceRecorder", "/sdcard/Record"
            },
            Extensions = new[] { "mp3", "m4a", "aac", "flac", "wav", "ogg", "opus", "amr", "wma", "mid" }
        },
        new()
        {
            Id = "documents",
            DisplayName = "Dokumente",
            Description = "PDF-, Office- und Textdateien aus dem Dokumentenordner.",
            Icon = "📄",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[]
            {
                "/sdcard/Documents", "/sdcard/Books", "/sdcard/eBooks", "/sdcard/Notes", "/sdcard/Scan"
            },
            Extensions = Array.Empty<string>()
        },
        new()
        {
            Id = "downloads",
            DisplayName = "Downloads",
            Description = "Der komplette Download-Ordner des Gerätes.",
            Icon = "⬇",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Download", "/sdcard/Downloads" },
            Extensions = Array.Empty<string>()
        },
        new()
        {
            Id = "messenger",
            DisplayName = "Messenger-Medien",
            Description = "Bilder, Videos und Sprachnachrichten von WhatsApp, Telegram, Signal und Threema.",
            Icon = "💬",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[]
            {
                "/sdcard/WhatsApp",
                "/sdcard/Android/media/com.whatsapp",
                "/sdcard/Android/media/com.whatsapp.w4b",
                "/sdcard/Android/media/org.telegram.messenger",
                "/sdcard/Telegram",
                "/sdcard/Android/media/org.thoughtcrime.securesms",
                "/sdcard/Android/media/ch.threema.app",
                "/sdcard/Signal"
            },
            Extensions = Array.Empty<string>(),
            Caveat = "Nur die Mediendateien; Chatverläufe liegen in den App-Daten und werden hier nicht erfasst."
        },
        new()
        {
            Id = "ringtones",
            DisplayName = "Klingeltöne & Töne",
            Description = "Eigene Klingeltöne, Benachrichtigungs- und Alarmtöne.",
            Icon = "🔔",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Ringtones", "/sdcard/Notifications", "/sdcard/Alarms" },
            Extensions = Array.Empty<string>(),
            SelectedByDefault = false
        },
        new()
        {
            Id = "bluetooth",
            DisplayName = "Bluetooth-Empfang",
            Description = "Per Bluetooth oder Nearby Share empfangene Dateien.",
            Icon = "📶",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Bluetooth", "/sdcard/NearbyShare" },
            Extensions = Array.Empty<string>(),
            SelectedByDefault = false
        },
        new()
        {
            Id = "otherfiles",
            DisplayName = "Sonstige Dateien",
            Description = "Alle übrigen Dateien im internen Speicher, ohne den Ordner Android.",
            Icon = "🗂",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { StorageRoot },
            Extensions = Array.Empty<string>(),
            IsCatchAll = true,
            SelectedByDefault = false,
            Caveat = "Kann je nach Gerät sehr umfangreich sein."
        },
        new()
        {
            Id = "appdatafolders",
            DisplayName = "App-Ordner im Speicher (Android/data)",
            Description = "Spielstände und App-Dateien aus /sdcard/Android/data und /sdcard/Android/obb.",
            Icon = "🎮",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Android/data", "/sdcard/Android/obb" },
            Extensions = Array.Empty<string>(),
            SelectedByDefault = false,
            Caveat = "Ab Android 11 sperren viele Geräte diesen Ordner auch für adb. Pixel Backup versucht es und meldet, was gelesen werden konnte."
        },
        new()
        {
            Id = "apps",
            DisplayName = "Apps (APK)",
            Description = "Die Installationsdateien aller selbst installierten Apps inklusive Split-APKs.",
            Icon = "📦",
            Kind = BackupCategoryKind.Apps,
            Caveat = "Gesichert werden die Apps selbst – nicht deren Daten."
        },
        new()
        {
            Id = "rootappdata",
            DisplayName = "App-Daten vollständig (Root)",
            Description = "Sichert /data/data je App als tar-Archiv – der einzige vollständige Weg für Spielstände und Chatverläufe.",
            Icon = "🔐",
            Kind = BackupCategoryKind.RootAppData,
            SelectedByDefault = false,
            RequiresRoot = true,
            Caveat = "Benötigt ein gerootetes Gerät (Magisk) oder ein userdebug-Abbild. Ohne Root nicht auswählbar."
        },
        new()
        {
            Id = "appdata",
            DisplayName = "App-Daten (klassisch, adb backup)",
            Description = "Sicherung über den alten Android-Sicherungsdienst.",
            Icon = "🗄",
            Kind = BackupCategoryKind.AppData,
            SelectedByDefault = false,
            Caveat = "Nur für ältere Geräte sinnvoll: Ab Android 12 liefern die meisten Apps nichts mehr, ab Android 13/14 ist der Weg praktisch tot."
        },
        new()
        {
            Id = "contacts",
            DisplayName = "Kontakte",
            Description = "Export der Kontaktdatenbank über den Android-Content-Provider.",
            Icon = "👤",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[]
            {
                "content://com.android.contacts/data/phones",
                "content://com.android.contacts/data/emails"
            },
            Import = ImportFormat.VCard,
            Caveat = "Erzeugt zusätzlich eine vCard-Datei (kontakte.vcf), die sich auf jedem neuen Telefon importieren lässt."
        },
        new()
        {
            Id = "sms",
            DisplayName = "SMS & MMS",
            Description = "Export der Kurznachrichten-Datenbank.",
            Icon = "✉",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[] { "content://sms", "content://mms" },
            Import = ImportFormat.SmsXml,
            SelectedByDefault = false,
            Caveat = "Erzeugt zusätzlich sms.xml im Format von „SMS Backup & Restore“ – damit lassen sich die Nachrichten auf dem neuen Telefon einspielen."
        },
        new()
        {
            Id = "calllog",
            DisplayName = "Anrufliste",
            Description = "Export der Anrufliste.",
            Icon = "📞",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[] { "content://call_log/calls" },
            Import = ImportFormat.CallsXml,
            SelectedByDefault = false,
            Caveat = "Erzeugt zusätzlich anrufliste.xml für „SMS Backup & Restore“."
        },
        new()
        {
            Id = "calendar",
            DisplayName = "Kalender",
            Description = "Termine aus dem Gerätekalender.",
            Icon = "📅",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[] { "content://com.android.calendar/events" },
            Import = ImportFormat.Ics,
            SelectedByDefault = false,
            Caveat = "Erzeugt zusätzlich kalender.ics zum Import auf dem neuen Telefon."
        },
        new()
        {
            Id = "settings",
            DisplayName = "Systemeinstellungen",
            Description = "Dokumentation der Einstellungen aus system, secure und global.",
            Icon = "⚙",
            Kind = BackupCategoryKind.Settings,
            Sources = new[] { "system", "secure", "global" },
            SelectedByDefault = false,
            Caveat = "Reine Dokumentation – wird nicht automatisch zurückgeschrieben."
        }
    };

    public static BackupCategory? ById(string id) =>
        All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public static int IndexOf(BackupCategory category)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (All[i].Id == category.Id)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>Alle Dateikategorien außer der Sammelkategorie.</summary>
    public static IEnumerable<BackupCategory> FileCategories =>
        All.Where(c => c.Kind == BackupCategoryKind.Files && !c.IsCatchAll);

    public static string DisplayNameOf(string categoryId) => ById(categoryId)?.DisplayName ?? categoryId;
}
