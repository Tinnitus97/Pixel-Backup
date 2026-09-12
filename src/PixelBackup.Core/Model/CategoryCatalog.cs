namespace PixelBackup.Core.Model;

/// <summary>Der fest eingebaute Katalog aller Sicherungsgruppen (zweisprachig).</summary>
public static class CategoryCatalog
{
    public const string StorageRoot = "/sdcard";

    public static IReadOnlyList<BackupCategory> All { get; } = new List<BackupCategory>
    {
        new()
        {
            Id = "photos",
            NameDe = "Fotos",
            NameEn = "Photos",
            DescriptionDe = "Kamerabilder, Screenshots und alle weiteren Bilddateien aus DCIM und Pictures.",
            DescriptionEn = "Camera shots, screenshots and every other image file in DCIM and Pictures.",
            Icon = "🖼",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/DCIM", "/sdcard/Pictures", "/sdcard/Camera", "/sdcard/Screenshots" },
            Extensions = new[] { "jpg", "jpeg", "png", "heic", "heif", "webp", "gif", "bmp", "dng", "raw", "tif", "tiff", "avif", "jfif" }
        },
        new()
        {
            Id = "videos",
            NameDe = "Videos",
            NameEn = "Videos",
            DescriptionDe = "Videoaufnahmen aus DCIM, Movies und Pictures.",
            DescriptionEn = "Video recordings from DCIM, Movies and Pictures.",
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
            NameDe = "Musik & Aufnahmen",
            NameEn = "Music & recordings",
            DescriptionDe = "Musikdateien, Sprachaufnahmen, Hörbücher und Podcasts.",
            DescriptionEn = "Music files, voice recordings, audio books and podcasts.",
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
            NameDe = "Dokumente",
            NameEn = "Documents",
            DescriptionDe = "PDF-, Office- und Textdateien aus dem Dokumentenordner.",
            DescriptionEn = "PDF, Office and text files from the documents folders.",
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
            NameDe = "Downloads",
            NameEn = "Downloads",
            DescriptionDe = "Der komplette Download-Ordner des Gerätes.",
            DescriptionEn = "The device's complete download folder.",
            Icon = "⬇",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Download", "/sdcard/Downloads" },
            Extensions = Array.Empty<string>()
        },
        new()
        {
            Id = "messenger",
            NameDe = "Messenger-Medien",
            NameEn = "Messenger media",
            DescriptionDe = "Bilder, Videos und Sprachnachrichten von WhatsApp, Telegram, Signal und Threema.",
            DescriptionEn = "Images, videos and voice messages from WhatsApp, Telegram, Signal and Threema.",
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
            CaveatDe = "Nur die Mediendateien; Chatverläufe liegen in den App-Daten und werden hier nicht erfasst.",
            CaveatEn = "Media files only; chat histories live in the app data and are not covered here."
        },
        new()
        {
            Id = "ringtones",
            NameDe = "Klingeltöne & Töne",
            NameEn = "Ringtones & sounds",
            DescriptionDe = "Eigene Klingeltöne, Benachrichtigungs- und Alarmtöne.",
            DescriptionEn = "Custom ringtones, notification and alarm sounds.",
            Icon = "🔔",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Ringtones", "/sdcard/Notifications", "/sdcard/Alarms" },
            Extensions = Array.Empty<string>(),
            SelectedByDefault = false
        },
        new()
        {
            Id = "bluetooth",
            NameDe = "Bluetooth-Empfang",
            NameEn = "Bluetooth & Nearby Share",
            DescriptionDe = "Per Bluetooth oder Nearby Share empfangene Dateien.",
            DescriptionEn = "Files received via Bluetooth or Nearby Share.",
            Icon = "📶",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Bluetooth", "/sdcard/NearbyShare" },
            Extensions = Array.Empty<string>(),
            SelectedByDefault = false
        },
        new()
        {
            Id = "otherfiles",
            NameDe = "Sonstige Dateien",
            NameEn = "Other files",
            DescriptionDe = "Alle übrigen Dateien im internen Speicher, ohne den Ordner Android.",
            DescriptionEn = "Everything else in internal storage, excluding the Android folder.",
            Icon = "🗂",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { StorageRoot },
            Extensions = Array.Empty<string>(),
            IsCatchAll = true,
            SelectedByDefault = false,
            CaveatDe = "Kann je nach Gerät sehr umfangreich sein.",
            CaveatEn = "Can be very large depending on the device."
        },
        new()
        {
            Id = "appdatafolders",
            NameDe = "App-Ordner im Speicher (Android/data)",
            NameEn = "App folders in storage (Android/data)",
            DescriptionDe = "Spielstände und App-Dateien aus /sdcard/Android/data und /sdcard/Android/obb.",
            DescriptionEn = "Saved games and app files from /sdcard/Android/data and /sdcard/Android/obb.",
            Icon = "🎮",
            Kind = BackupCategoryKind.Files,
            RemoteDirectories = new[] { "/sdcard/Android/data", "/sdcard/Android/obb" },
            Extensions = Array.Empty<string>(),
            SelectedByDefault = false,
            CaveatDe = "Ab Android 11 sperren viele Geräte diesen Ordner auch für adb. Pixel Backup versucht es und meldet, was gelesen werden konnte.",
            CaveatEn = "From Android 11 on many devices block this folder even for adb. Pixel Backup tries anyway and reports what it could read."
        },
        new()
        {
            Id = "apps",
            NameDe = "Apps (APK)",
            NameEn = "Apps (APK)",
            DescriptionDe = "Die Installationsdateien aller selbst installierten Apps inklusive Split-APKs.",
            DescriptionEn = "The install packages of every user-installed app, split APKs included.",
            Icon = "📦",
            Kind = BackupCategoryKind.Apps,
            CaveatDe = "Gesichert werden die Apps selbst – nicht deren Daten.",
            CaveatEn = "This backs up the apps themselves – not their data."
        },
        new()
        {
            Id = "rootappdata",
            NameDe = "App-Daten vollständig (Root)",
            NameEn = "Full app data (root)",
            DescriptionDe = "Sichert /data/data je App als tar-Archiv – der einzige vollständige Weg für Spielstände und Chatverläufe.",
            DescriptionEn = "Stores /data/data per app as a tar archive – the only complete route for saved games and chat histories.",
            Icon = "🔐",
            Kind = BackupCategoryKind.RootAppData,
            SelectedByDefault = false,
            RequiresRoot = true,
            CaveatDe = "Benötigt ein gerootetes Gerät (Magisk) oder ein userdebug-Abbild. Ohne Root nicht auswählbar.",
            CaveatEn = "Requires a rooted device (Magisk) or a userdebug build. Not selectable without root."
        },
        new()
        {
            Id = "appdata",
            NameDe = "App-Daten (klassisch, adb backup)",
            NameEn = "App data (legacy, adb backup)",
            DescriptionDe = "Sicherung über den alten Android-Sicherungsdienst.",
            DescriptionEn = "Backup through the old Android backup service.",
            Icon = "🗄",
            Kind = BackupCategoryKind.AppData,
            SelectedByDefault = false,
            CaveatDe = "Nur für ältere Geräte sinnvoll: Ab Android 12 liefern die meisten Apps nichts mehr, ab Android 13/14 ist der Weg praktisch tot.",
            CaveatEn = "Only useful on older devices: from Android 12 most apps return nothing, and from Android 13/14 the route is effectively dead."
        },
        new()
        {
            Id = "contacts",
            NameDe = "Kontakte",
            NameEn = "Contacts",
            DescriptionDe = "Export der Kontaktdatenbank über den Android-Content-Provider.",
            DescriptionEn = "Exports the contact database through the Android content provider.",
            Icon = "👤",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[]
            {
                "content://com.android.contacts/data/phones",
                "content://com.android.contacts/data/emails"
            },
            Import = ImportFormat.VCard,
            CaveatDe = "Erzeugt zusätzlich eine vCard-Datei (kontakte.vcf), die sich auf jedem neuen Telefon importieren lässt.",
            CaveatEn = "Also produces a vCard file (kontakte.vcf) that any new phone can import."
        },
        new()
        {
            Id = "sms",
            NameDe = "SMS & MMS",
            NameEn = "SMS & MMS",
            DescriptionDe = "Export der Kurznachrichten-Datenbank.",
            DescriptionEn = "Exports the text message database.",
            Icon = "✉",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[] { "content://sms", "content://mms" },
            Import = ImportFormat.SmsXml,
            SelectedByDefault = false,
            CaveatDe = "Erzeugt zusätzlich sms.xml im Format von „SMS Backup & Restore“ – damit lassen sich die Nachrichten auf dem neuen Telefon einspielen.",
            CaveatEn = "Also produces sms.xml in the \"SMS Backup & Restore\" format so the messages can be replayed on the new phone."
        },
        new()
        {
            Id = "calllog",
            NameDe = "Anrufliste",
            NameEn = "Call log",
            DescriptionDe = "Export der Anrufliste.",
            DescriptionEn = "Exports the call log.",
            Icon = "📞",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[] { "content://call_log/calls" },
            Import = ImportFormat.CallsXml,
            SelectedByDefault = false,
            CaveatDe = "Erzeugt zusätzlich anrufliste.xml für „SMS Backup & Restore“.",
            CaveatEn = "Also produces anrufliste.xml for \"SMS Backup & Restore\"."
        },
        new()
        {
            Id = "calendar",
            NameDe = "Kalender",
            NameEn = "Calendar",
            DescriptionDe = "Termine aus dem Gerätekalender.",
            DescriptionEn = "Appointments from the device calendar.",
            Icon = "📅",
            Kind = BackupCategoryKind.ContentProvider,
            Sources = new[] { "content://com.android.calendar/events" },
            Import = ImportFormat.Ics,
            SelectedByDefault = false,
            CaveatDe = "Erzeugt zusätzlich kalender.ics zum Import auf dem neuen Telefon.",
            CaveatEn = "Also produces kalender.ics for import on the new phone."
        },
        new()
        {
            Id = "settings",
            NameDe = "Systemeinstellungen",
            NameEn = "System settings",
            DescriptionDe = "Dokumentation der Einstellungen aus system, secure und global.",
            DescriptionEn = "Documents the settings from system, secure and global.",
            Icon = "⚙",
            Kind = BackupCategoryKind.Settings,
            Sources = new[] { "system", "secure", "global" },
            SelectedByDefault = false,
            CaveatDe = "Reine Dokumentation – wird nicht automatisch zurückgeschrieben.",
            CaveatEn = "Documentation only – never written back automatically."
        }
    };

    public static BackupCategory? ById(string id) =>
        All.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));

    public static int IndexOf(BackupCategory category) => IndexOf(category.Id);

    /// <summary>Position im Katalog; unbekannte Kennungen landen am Ende.</summary>
    public static int IndexOf(string categoryId)
    {
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i].Id, categoryId, StringComparison.OrdinalIgnoreCase))
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
