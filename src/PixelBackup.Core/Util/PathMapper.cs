using System.Text;

namespace PixelBackup.Core.Util;

/// <summary>
/// Bildet Gerätepfade (<c>/sdcard/DCIM/foto.jpg</c>) auf lokale, relative Sicherungspfade ab
/// (<c>files/sdcard/DCIM/foto.jpg</c>) und umgekehrt.
/// </summary>
public static class PathMapper
{
    public const string FilesFolder = "files";
    public const string AppsFolder = "apps";
    public const string DataFolder = "data";
    public const string AppDataFolder = "appdata";
    public const string RootAppDataFolder = "appdata-root";

    private static readonly char[] InvalidNameChars = { '<', '>', ':', '"', '|', '?', '*', '\\' };

    /// <summary>Relativer Pfad innerhalb des Sicherungsordners – immer mit '/' als Trenner.</summary>
    public static string ToRelativeBackupPath(string remotePath)
    {
        var segments = SplitRemote(remotePath);
        var builder = new StringBuilder(FilesFolder);
        foreach (var segment in segments)
        {
            builder.Append('/').Append(SanitizeSegment(segment));
        }

        return builder.ToString();
    }

    public static string SanitizeSegment(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            builder.Append(Array.IndexOf(InvalidNameChars, c) >= 0 || char.IsControl(c) ? '_' : c);
        }

        var result = builder.ToString().TrimEnd(' ', '.');
        return result.Length == 0 ? "_" : result;
    }

    /// <summary>Wandelt einen relativen Sicherungspfad in einen absoluten Pfad im Dateisystem um.</summary>
    public static string ToLocalPath(string setDirectory, string relativeBackupPath)
    {
        var parts = relativeBackupPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(new[] { setDirectory }.Concat(parts).ToArray());
    }

    /// <summary>Ermittelt das Verzeichnis eines Gerätepfades (ohne abschließenden Schrägstrich).</summary>
    public static string RemoteDirectory(string remotePath)
    {
        var index = remotePath.LastIndexOf('/');
        return index <= 0 ? "/" : remotePath[..index];
    }

    public static string RemoteFileName(string remotePath)
    {
        var index = remotePath.LastIndexOf('/');
        return index < 0 ? remotePath : remotePath[(index + 1)..];
    }

    public static string RemoteExtension(string remotePath)
    {
        var name = RemoteFileName(remotePath);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[(dot + 1)..].ToLowerInvariant();
    }

    /// <summary>Fügt vor der Dateiendung ein Suffix ein (für die Konfliktstrategie "beide behalten").</summary>
    public static string AppendSuffix(string remotePath, string suffix)
    {
        var directory = RemoteDirectory(remotePath);
        var name = RemoteFileName(remotePath);
        var dot = name.LastIndexOf('.');
        var newName = dot <= 0 ? name + suffix : name[..dot] + suffix + name[dot..];
        return directory == "/" ? "/" + newName : directory + "/" + newName;
    }

    public static bool IsInsideRemoteDirectory(string remotePath, string remoteDirectory)
    {
        var normalized = remoteDirectory.TrimEnd('/');
        if (normalized.Length == 0)
        {
            return true;
        }

        return remotePath.Length > normalized.Length
               && remotePath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase)
               && remotePath[normalized.Length] == '/';
    }

    private static IEnumerable<string> SplitRemote(string remotePath) =>
        remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
