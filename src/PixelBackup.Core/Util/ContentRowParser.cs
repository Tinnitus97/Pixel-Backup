using System.Text;
using System.Text.RegularExpressions;

namespace PixelBackup.Core.Util;

/// <summary>
/// Wandelt die Ausgabe von <c>adb shell content query</c> ("Row: 0 name=Max, number=0170…")
/// in eine Tabelle um, damit Kontakte, SMS und Anrufliste als CSV lesbar sind.
/// </summary>
public static class ContentRowParser
{
    private static readonly Regex KeyPattern = new(@"(?:^|,\s)(?<key>[A-Za-z0-9_]+)=", RegexOptions.Compiled);

    public static List<Dictionary<string, string>> Parse(string output)
    {
        var rows = new List<Dictionary<string, string>>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (!line.StartsWith("Row:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var space = line.IndexOf(' ', 5);
            var payload = space > 0 ? line[(space + 1)..] : string.Empty;
            if (payload.Length == 0)
            {
                continue;
            }

            var matches = KeyPattern.Matches(payload);
            if (matches.Count == 0)
            {
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var valueStart = match.Index + match.Length;
                var valueEnd = i + 1 < matches.Count ? matches[i + 1].Index : payload.Length;
                var value = payload[valueStart..valueEnd].Trim();
                row[match.Groups["key"].Value] = value;
            }

            rows.Add(row);
        }

        return rows;
    }

    public static string ToCsv(IReadOnlyList<Dictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (!columns.Contains(key))
                {
                    columns.Add(key);
                }
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(";", columns.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(";", columns.Select(c => Escape(row.GetValueOrDefault(c, string.Empty)))));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(';') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
