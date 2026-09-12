using System.Globalization;
using PixelBackup.Core.Localization;

namespace PixelBackup.Core.Util;

/// <summary>Formatierungshilfen für die Oberfläche und Berichte.</summary>
public static class Humanize
{
    private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long bytes)
    {
        if (bytes < 0)
        {
            return "–";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024d && unit < Units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        var digits = unit == 0 ? 0 : value >= 100 ? 0 : value >= 10 ? 1 : 2;
        return value.ToString("N" + digits, CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    public static string Speed(double bytesPerSecond)
    {
        if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond <= 0)
        {
            return "–";
        }

        return Bytes((long)bytesPerSecond) + "/s";
    }

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            return "–";
        }

        if (span.TotalHours >= 1)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}:{2:00} h", (int)span.TotalHours, span.Minutes, span.Seconds);
        }

        if (span.TotalMinutes >= 1)
        {
            return string.Format(CultureInfo.CurrentCulture, "{0}:{1:00} min", span.Minutes, span.Seconds);
        }

        return string.Format(CultureInfo.CurrentCulture, "{0} s", Math.Max(0, (int)span.TotalSeconds));
    }

    public static string Count(int value, string singular, string plural) =>
        value == 1 ? $"1 {singular}" : $"{value.ToString("N0", CultureInfo.CurrentCulture)} {plural}";

    /// <summary>"12.481 Elemente" bzw. "12,481 items".</summary>
    public static string Items(int value) =>
        Count(value, Loc.Tr("Element", "item"), Loc.Tr("Elemente", "items"));

    /// <summary>"3 Dateien" bzw. "3 files".</summary>
    public static string Files(int value) =>
        Count(value, Loc.Tr("Datei", "file"), Loc.Tr("Dateien", "files"));

    /// <summary>"2 Apps" – in beiden Sprachen gleich, nur der Plural unterscheidet sich.</summary>
    public static string Apps(int value) => Count(value, "App", Loc.Tr("Apps", "apps"));
}
