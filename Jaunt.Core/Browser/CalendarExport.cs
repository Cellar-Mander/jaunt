using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Jaunt.Core.Browser;

/// <summary>How Add to calendar adds an opening by default. Saved by number, so only ever append.</summary>
public enum CalendarFormat
{
    /// <summary>An RFC 5545 <c>.ics</c> file, opened with the system's calendar app.</summary>
    Ics,

    /// <summary>Google Calendar's new-event page.</summary>
    Google,

    /// <summary>Outlook.com's new-event page.</summary>
    Outlook,
}

/// <summary>One opening as Add to calendar exports it. Text is as the listing gives it; <see cref="CalendarExport"/> cuts and escapes it.</summary>
/// <param name="Uid">Stays the same for the same opening, so adding it again updates rather than duplicates it where the calendar supports that.</param>
/// <param name="End">Null, or not after <paramref name="Start"/>, when the listing gives no closing time; the event is then a moment at its start.</param>
/// <param name="Url">The listing's page, if any.</param>
public sealed record CalendarEvent(string Uid, string Title, DateTimeOffset Start, DateTimeOffset? End, string? Location, string? Description, Uri? Url);

/// <summary>
/// Turns an opening into an <c>.ics</c> file or a Google Calendar or Outlook.com new-event link. Listing
/// text is untrusted, so each field is cut to a cap first and every step is linear in its length.
/// </summary>
public static class CalendarExport
{
    /// <summary>The most characters of a title, location or description kept, before escaping.</summary>
    public const int MaxTitle = 200;

    /// <inheritdoc cref="MaxTitle"/>
    public const int MaxLocation = 200;

    /// <inheritdoc cref="MaxTitle"/>
    public const int MaxDescription = 2000;

    /// <summary>The most characters of a description kept in a link, so the link stays a length browsers and servers accept.</summary>
    public const int MaxLinkDescription = 600;

    /// <summary>The longest content line in octets before it is folded (RFC 5545 §3.1).</summary>
    private const int FoldOctets = 75;

    private const string Crlf = "\r\n";

    /// <summary>
    /// The opening Add to calendar exports from an entry's listed openings: the first with a known start.
    /// Null for a 24/7 listing and when no listed opening says when it starts ("open now", "open until 1am").
    /// </summary>
    /// <param name="listed">Earliest first, as <see cref="Schedules.Listed"/> gives them.</param>
    public static Occurrence? Exportable(IReadOnlyList<Occurrence> listed, bool alwaysOpen)
    {
        if (alwaysOpen)
            return null;
        foreach (var occurrence in listed)
        {
            if (occurrence.Start is not null)
                return occurrence;
        }

        return null;
    }

    /// <summary>A UID for one opening of one listing, the same each time it is exported.</summary>
    public static string UidFor(string source, string id, DateTimeOffset start) =>
        $"{Hash($"{source}\n{id}")}-{Stamp(start)}@jaunt";

    /// <summary>A file name for the event's <c>.ics</c>, safe on every file system whatever the listing's text.</summary>
    public static string FileName(CalendarEvent calendarEvent) => $"jaunt-{Hash(calendarEvent.Uid)}.ics";

    /// <summary>The event as an RFC 5545 calendar with one VEVENT: UTC times, CRLF line ends, escaped and folded text.</summary>
    /// <param name="now">When it is exported, for DTSTAMP.</param>
    public static string Ics(CalendarEvent calendarEvent, DateTimeOffset now)
    {
        var text = new StringBuilder();
        Line(text, "BEGIN:VCALENDAR");
        Line(text, "VERSION:2.0");
        Line(text, "PRODID:-//Jaunt//Jaunt//EN");
        Line(text, "CALSCALE:GREGORIAN");
        Line(text, "METHOD:PUBLISH");
        Line(text, "BEGIN:VEVENT");
        Line(text, "UID:" + Escape(calendarEvent.Uid));
        Line(text, "DTSTAMP:" + Stamp(now));
        Line(text, "DTSTART:" + Stamp(calendarEvent.Start));
        if (EndOf(calendarEvent) is { } end)
            Line(text, "DTEND:" + Stamp(end));
        Line(text, "SUMMARY:" + Escape(Title(calendarEvent)));
        if (Cut(calendarEvent.Location, MaxLocation) is { Length: > 0 } location)
            Line(text, "LOCATION:" + Escape(location));
        if (Details(calendarEvent, MaxDescription) is { Length: > 0 } details)
            Line(text, "DESCRIPTION:" + Escape(details));
        if (WebUrl(calendarEvent.Url) is { } url)
            Line(text, "URL:" + url);
        Line(text, "END:VEVENT");
        Line(text, "END:VCALENDAR");
        return text.ToString();
    }

    /// <summary>Google Calendar's new-event page with the event filled in. With no closing time the event ends at its start.</summary>
    public static Uri GoogleLink(CalendarEvent calendarEvent)
    {
        var query = new StringBuilder("https://calendar.google.com/calendar/render?action=TEMPLATE");
        Parameter(query, "text", Title(calendarEvent));
        Parameter(query, "dates", $"{Stamp(calendarEvent.Start)}/{Stamp(EndOf(calendarEvent) ?? calendarEvent.Start)}");
        Parameter(query, "location", Cut(calendarEvent.Location, MaxLocation));
        Parameter(query, "details", Details(calendarEvent, MaxLinkDescription));
        return new Uri(query.ToString());
    }

    /// <summary>Outlook.com's new-event page with the event filled in. With no closing time the event ends at its start.</summary>
    public static Uri OutlookLink(CalendarEvent calendarEvent)
    {
        var query = new StringBuilder("https://outlook.live.com/calendar/0/action/compose?rru=addevent");
        Parameter(query, "subject", Title(calendarEvent));
        Parameter(query, "startdt", Iso(calendarEvent.Start));
        Parameter(query, "enddt", Iso(EndOf(calendarEvent) ?? calendarEvent.Start));
        Parameter(query, "location", Cut(calendarEvent.Location, MaxLocation));
        Parameter(query, "body", Details(calendarEvent, MaxLinkDescription));
        return new Uri(query.ToString());
    }

    private static DateTimeOffset? EndOf(CalendarEvent calendarEvent) =>
        calendarEvent.End is { } end && end > calendarEvent.Start ? end : null;

    private static string Title(CalendarEvent calendarEvent) =>
        Cut(calendarEvent.Title, MaxTitle) is { Length: > 0 } title ? title : "(untitled)";

    /// <summary>The page link, then the description, so a calendar that ignores URL still shows it.</summary>
    private static string Details(CalendarEvent calendarEvent, int max)
    {
        var url = WebUrl(calendarEvent.Url);
        var description = Cut(calendarEvent.Description, max);
        return url is null ? description ?? ""
            : description is not { Length: > 0 } ? url
            : $"{url}\n\n{description}";
    }

    /// <summary>The link if it is http or https; anything else could run something when clicked in a calendar.</summary>
    private static string? WebUrl(Uri? url) =>
        url is { IsAbsoluteUri: true } && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp) ? url.AbsoluteUri : null;

    /// <summary>
    /// The text trimmed, with control characters but line feeds and tabs dropped and CRLF or CR as a line
    /// feed, cut to <paramref name="max"/> characters without splitting a surrogate pair.
    /// </summary>
    private static string? Cut(string? text, int max)
    {
        if (text is null)
            return null;
        // Stryker disable once Linq : the capacity only sizes the buffer; what is appended is the same
        var cleaned = new StringBuilder(Math.Min(text.Length, max));
        for (var i = 0; i < text.Length && cleaned.Length < max; i++)
        {
            var c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    continue;
                c = '\n';
            }
            else if (char.IsControl(c) && c is not '\n' and not '\t')
            {
                continue;
            }

            cleaned.Append(c);
        }

        if (cleaned.Length > 0 && char.IsHighSurrogate(cleaned[^1]))
            cleaned.Length--;
        return cleaned.ToString().Trim();
    }

    /// <summary>TEXT escaping (RFC 5545 §3.3.11): backslash, semicolon, comma and line feed.</summary>
    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            _ = c switch
            {
                '\\' => escaped.Append(@"\\"),
                ';' => escaped.Append(@"\;"),
                ',' => escaped.Append(@"\,"),
                '\n' => escaped.Append(@"\n"),
                _ => escaped.Append(c),
            };
        }

        return escaped.ToString();
    }

    /// <summary>Appends a content line, folded so no line is longer than 75 octets of UTF-8, never inside a character.</summary>
    private static void Line(StringBuilder text, string line)
    {
        var octets = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var pair = char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]);
            var size = pair ? 4 : Encoding.UTF8.GetByteCount(line.AsSpan(i, 1));
            if (octets + size > FoldOctets)
            {
                // The continuation line's leading space counts towards its 75
                text.Append(Crlf).Append(' ');
                octets = 1;
            }

            text.Append(line[i]);
            if (pair)
                text.Append(line[++i]);
            octets += size;
        }

        text.Append(Crlf);
    }

    private static void Parameter(StringBuilder query, string name, string? value)
    {
        if (value is not { Length: > 0 })
            return;
        query.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
    }

    private static string Stamp(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
