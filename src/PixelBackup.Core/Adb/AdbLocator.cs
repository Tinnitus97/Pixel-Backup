using System.Runtime.InteropServices;

namespace PixelBackup.Core.Adb;

/// <summary>Sucht die ausführbare adb-Datei an den üblichen Stellen.</summary>
public static class AdbLocator
{
    public static string ExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "adb.exe" : "adb";

    /// <summary>Liefert alle Kandidatenpfade in absteigender Priorität.</summary>
    public static IEnumerable<string> CandidatePaths(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath!;
        }

        var appDirectory = AppContext.BaseDirectory;
        yield return Path.Combine(appDirectory, "platform-tools", ExecutableName);
        yield return Path.Combine(appDirectory, ExecutableName);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "Android", "Sdk", "platform-tools", ExecutableName);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            yield return Path.Combine(userProfile, "Android", "Sdk", "platform-tools", ExecutableName);
            yield return Path.Combine(userProfile, "AppData", "Local", "Android", "Sdk", "platform-tools", ExecutableName);
            yield return Path.Combine(userProfile, "Library", "Android", "sdk", "platform-tools", ExecutableName);
        }

        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                          ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(androidHome))
        {
            yield return Path.Combine(androidHome, "platform-tools", ExecutableName);
        }

        yield return Path.Combine(@"C:\Program Files\Android\platform-tools", ExecutableName);
        yield return Path.Combine(@"C:\platform-tools", ExecutableName);
        yield return "/usr/bin/adb";
        yield return "/usr/local/bin/adb";
        yield return "/opt/homebrew/bin/adb";

        foreach (var fromPath in FromEnvironmentPath())
        {
            yield return fromPath;
        }
    }

    /// <summary>Erster existierender Kandidat oder <c>null</c>.</summary>
    public static string? Locate(string? configuredPath = null)
    {
        foreach (var candidate in CandidatePaths(configuredPath))
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception)
            {
                // Ungültige Pfadangaben werden übersprungen.
            }
        }

        return null;
    }

    private static IEnumerable<string> FromEnvironmentPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            yield break;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), ExecutableName);
            yield return candidate;
        }
    }
}
