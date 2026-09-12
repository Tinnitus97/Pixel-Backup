using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PixelBackup.Core.Util;

/// <summary>
/// Erzeugt aus den Rohdaten der Content-Provider Dateien, die sich auf einem
/// <b>neuen</b> Telefon direkt importieren lassen: vCard, ICS und die XML-Formate
/// der weit verbreiteten App „SMS Backup &amp; Restore“.
/// </summary>
public static class ImportExporters
{
    private const string NewLine = "\r\n";

    // ------------------------------------------------------------------ Kontakte

    /// <summary>Baut eine vCard-Datei (Version 3.0) aus Telefon- und E-Mail-Datensätzen.</summary>
    public static string BuildVCards(
        IReadOnlyList<Dictionary<string, string>> phoneRows,
        IReadOnlyList<Dictionary<string, string>>? emailRows = null)
    {
        var contacts = new List<VCardContact>();
        var index = new Dictionary<string, VCardContact>(StringComparer.OrdinalIgnoreCase);

        VCardContact Resolve(Dictionary<string, string> row)
        {
            var name = Value(row, "display_name", "display_name_alt", "data1");
            var key = Value(row, "contact_id", "raw_contact_id");
            if (string.IsNullOrEmpty(key))
            {
                key = "name:" + name;
            }

            if (!index.TryGetValue(key, out var contact))
            {
                contact = new VCardContact(string.IsNullOrWhiteSpace(name) ? "Unbekannt" : name);
                index[key] = contact;
                contacts.Add(contact);
            }
            else if (contact.Name == "Unbekannt" && !string.IsNullOrWhiteSpace(name))
            {
                contact.Name = name;
            }

            return contact;
        }

        foreach (var row in phoneRows)
        {
            var number = Value(row, "data1", "number");
            if (string.IsNullOrWhiteSpace(number))
            {
                continue;
            }

            var contact = Resolve(row);
            if (!contact.Numbers.Contains(number))
            {
                contact.Numbers.Add(number);
            }
        }

        foreach (var row in emailRows ?? new List<Dictionary<string, string>>())
        {
            var mail = Value(row, "data1", "address");
            if (string.IsNullOrWhiteSpace(mail) || !mail.Contains('@'))
            {
                continue;
            }

            var contact = Resolve(row);
            if (!contact.Mails.Contains(mail))
            {
                contact.Mails.Add(mail);
            }
        }

        var builder = new StringBuilder();
        foreach (var contact in contacts)
        {
            builder.Append("BEGIN:VCARD").Append(NewLine);
            builder.Append("VERSION:3.0").Append(NewLine);
            builder.Append("N:").Append(EscapeVCard(contact.Name)).Append(";;;;").Append(NewLine);
            builder.Append("FN:").Append(EscapeVCard(contact.Name)).Append(NewLine);

            foreach (var number in contact.Numbers)
            {
                builder.Append("TEL;TYPE=CELL:").Append(EscapeVCard(number)).Append(NewLine);
            }

            foreach (var mail in contact.Mails)
            {
                builder.Append("EMAIL;TYPE=INTERNET:").Append(EscapeVCard(mail)).Append(NewLine);
            }

            builder.Append("END:VCARD").Append(NewLine);
        }

        return builder.ToString();
    }

    private sealed class VCardContact
    {
        public VCardContact(string name) => Name = name;

        public string Name { get; set; }

        public List<string> Numbers { get; } = new();

        public List<string> Mails { get; } = new();
    }

    // --------------------------------------------------------------- Nachrichten

    /// <summary>XML im Format von „SMS Backup &amp; Restore“.</summary>
    public static string BuildSmsXml(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var messages = new List<XElement>();

        foreach (var row in rows)
        {
            var address = Value(row, "address");
            var body = Value(row, "body");
            if (string.IsNullOrEmpty(address) && string.IsNullOrEmpty(body))
            {
                continue;
            }

            messages.Add(new XElement(
                "sms",
                new XAttribute("protocol", ValueOr(row, "protocol", "0")),
                new XAttribute("address", address),
                new XAttribute("date", ValueOr(row, "date", "0")),
                new XAttribute("type", ValueOr(row, "type", "1")),
                new XAttribute("subject", NullIfEmpty(Value(row, "subject"))),
                new XAttribute("body", body),
                new XAttribute("toa", "null"),
                new XAttribute("sc_toa", "null"),
                new XAttribute("service_center", NullIfEmpty(Value(row, "service_center"))),
                new XAttribute("read", ValueOr(row, "read", "1")),
                new XAttribute("status", ValueOr(row, "status", "-1")),
                new XAttribute("locked", ValueOr(row, "locked", "0")),
                new XAttribute("date_sent", ValueOr(row, "date_sent", "0")),
                new XAttribute("readable_date", ReadableDate(Value(row, "date")))));
        }

        return Document(new XElement("smses", new XAttribute("count", messages.Count), messages));
    }

    /// <summary>XML der Anrufliste im Format von „SMS Backup &amp; Restore“.</summary>
    public static string BuildCallsXml(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var calls = new List<XElement>();

        foreach (var row in rows)
        {
            var number = Value(row, "number");
            if (string.IsNullOrEmpty(number))
            {
                continue;
            }

            calls.Add(new XElement(
                "call",
                new XAttribute("number", number),
                new XAttribute("duration", ValueOr(row, "duration", "0")),
                new XAttribute("date", ValueOr(row, "date", "0")),
                new XAttribute("type", ValueOr(row, "type", "1")),
                new XAttribute("presentation", ValueOr(row, "presentation", "1")),
                new XAttribute("readable_date", ReadableDate(Value(row, "date"))),
                new XAttribute("contact_name", NullIfEmpty(Value(row, "name", "cached_name")))));
        }

        return Document(new XElement("calls", new XAttribute("count", calls.Count), calls));
    }

    // ------------------------------------------------------------------ Kalender

    /// <summary>Kalenderdatei (.ics) aus den Terminen des Gerätekalenders.</summary>
    public static string BuildIcs(IReadOnlyList<Dictionary<string, string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append("BEGIN:VCALENDAR").Append(NewLine);
        builder.Append("VERSION:2.0").Append(NewLine);
        builder.Append("PRODID:-//Pixel Backup//Android Sicherung//DE").Append(NewLine);
        builder.Append("CALSCALE:GREGORIAN").Append(NewLine);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        foreach (var row in rows)
        {
            var title = Value(row, "title", "eventTitle");
            var start = ParseMilliseconds(Value(row, "dtstart"));
            if (start is null)
            {
                continue;
            }

            var allDay = Value(row, "allDay") == "1";
            var end = ParseMilliseconds(Value(row, "dtend"));

            builder.Append("BEGIN:VEVENT").Append(NewLine);
            builder.Append("UID:").Append(ValueOr(row, "_id", Guid.NewGuid().ToString("n")))
                .Append("@pixel-backup").Append(NewLine);
            builder.Append("DTSTAMP:").Append(stamp).Append(NewLine);
            builder.Append(FormatIcsDate("DTSTART", start.Value, allDay)).Append(NewLine);

            if (end is not null)
            {
                builder.Append(FormatIcsDate("DTEND", end.Value, allDay)).Append(NewLine);
            }

            builder.Append("SUMMARY:").Append(EscapeVCard(string.IsNullOrWhiteSpace(title) ? "Termin" : title)).Append(NewLine);

            var location = Value(row, "eventLocation");
            if (!string.IsNullOrWhiteSpace(location))
            {
                builder.Append("LOCATION:").Append(EscapeVCard(location)).Append(NewLine);
            }

            var description = Value(row, "description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.Append("DESCRIPTION:").Append(EscapeVCard(description)).Append(NewLine);
            }

            builder.Append("END:VEVENT").Append(NewLine);
        }

        builder.Append("END:VCALENDAR").Append(NewLine);
        return builder.ToString();
    }

    // -------------------------------------------------------------------- Helfer

    private static string FormatIcsDate(string field, DateTimeOffset value, bool allDay) => allDay
        ? $"{field};VALUE=DATE:{value.UtcDateTime:yyyyMMdd}"
        : $"{field}:{value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}";

    private static DateTimeOffset? ParseMilliseconds(string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) && milliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : null;

    private static string ReadableDate(string milliseconds)
    {
        var value = ParseMilliseconds(milliseconds);
        return value is null
            ? string.Empty
            : value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string Value(Dictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && value.Length > 0 && value != "NULL" && value != "null")
            {
                return value;
            }
        }

        return string.Empty;
    }

    /// <summary>Wert des Feldes oder ein Ersatzwert, wenn das Feld fehlt oder leer ist.</summary>
    private static string ValueOr(Dictionary<string, string> row, string key, string fallback)
    {
        var value = Value(row, key);
        return value.Length == 0 ? fallback : value;
    }

    private static string NullIfEmpty(string value) => value.Length == 0 ? "null" : value;

    private static string EscapeVCard(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace(";", "\;", StringComparison.Ordinal)
        .Replace(",", "\\,", StringComparison.Ordinal)
        .Replace("\r\n", "\\n", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Document(XElement root)
    {
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        document.Save(writer);
        return writer.ToString();
    }
}
