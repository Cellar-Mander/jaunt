using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jaunt.Core.Listings;

/// <summary>
/// Reads the FFXIV RP Event Calendar's events for the next few weeks (<see cref="Url"/>) and records
/// their raw fields in the listing cache. The rules are in <c>docs/design.md</c> under "FFXIV RP Event Calendar".
/// </summary>
/// <remarks>
/// The response holds one row per occurrence, about 500 rows and 700 KB, which this groups into one
/// event per calendar ID in a single pass. A row that doesn't have the expected shape is skipped
/// rather than failing the rest. Each row's description and contacts hold Discord handles, so they
/// are never read. The cache keeps the name, server, location and rating as the calendar sent them,
/// so a change to how they are read applies to cached events without fetching again.
/// </remarks>
public static class RpEventCalendar
{
    public const string Url = "https://api.ffxiv-rp.org/api/Events/GetWeekTranslatableEvents";

    /// <summary>The calendar's site, credited beside its listings.</summary>
    public static readonly Uri SiteUrl = new("https://ffxiv-rp.org/");

    /// <summary>The calendar's listings are under this licence, which needs crediting wherever they show.</summary>
    public static readonly Uri LicenceUrl = new("https://creativecommons.org/licenses/by-sa/4.0/");

    public const string LicenceName = "CC BY-SA 4.0";

    /// <summary>One event as the calendar sends it, with only the fields Jaunt reads.</summary>
    /// <param name="Id">The calendar's ID for the event, shared by its occurrences.</param>
    /// <param name="Name">Its name as sent, rating tag included; null if missing.</param>
    /// <param name="Server">Its world as sent; null if missing.</param>
    /// <param name="Location">Its location as sent; null if missing.</param>
    /// <param name="Rating">Its rating as sent; null if missing.</param>
    /// <param name="LinkTexts">Its page link as sent, if any; checked only when shown.</param>
    /// <param name="Openings">Its occurrences in the response, earliest first.</param>
    /// <param name="OneOff">The calendar says it doesn't recur.</param>
    /// <param name="LastValidated">The day a moderator last checked it, or null if never.</param>
    public sealed record Event(
        string Id,
        string? Name,
        string? Server,
        string? Location,
        string? Rating,
        IReadOnlyList<string> LinkTexts,
        IReadOnlyList<ListingOpening> Openings,
        bool OneOff,
        DateOnly? LastValidated);

    /// <summary>Reads the API's response.</summary>
    /// <exception cref="JsonException">The text is not a JSON array.</exception>
    public static IReadOnlyList<Event> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException("The FFXIV RP Event Calendar did not send a list of events");

        // Occurrences of one event repeat its other fields, so the first row read is kept for those
        var rows = new Dictionary<string, (RowData Row, List<ListingOpening> Openings)>(StringComparer.Ordinal);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (Read(element) is not var (row, opening))
                continue;

            var id = row.UId!.Trim();
            if (!rows.TryGetValue(id, out var entry))
            {
                entry = (row, []);
                rows.Add(id, entry);
            }

            if (!entry.Openings.Contains(opening))
                entry.Openings.Add(opening);
        }

        var events = new List<Event>(rows.Count);
        foreach (var (id, (row, openings)) in rows)
            events.Add(ToEvent(id, row, openings));
        return events;
    }

    private static Event ToEvent(string id, RowData row, List<ListingOpening> openings) =>
        new(
            id,
            row.EventName,
            row.Server,
            row.Location,
            row.Rating,
            row.EventUrl is { } url ? [url] : [],
            [.. openings.OrderBy(o => o.Start)],
            row.IsRecurring == false,
            LastValidated(row.LastValidated));

    /// <summary>
    /// Records that the calendar listed these events now, keeping the name as the listing's title,
    /// the server as the world it is listed on, and the location, rating and link, all as sent.
    /// </summary>
    public static void Record(ListingCache cache, IEnumerable<Event> events, DateTimeOffset now)
    {
        foreach (var calendarEvent in events)
        {
            var listing = cache.Seen(ListingSource.RpEventCalendar, calendarEvent.Id, calendarEvent.Name ?? "", now, calendarEvent.OneOff);
            listing.ListedOn = calendarEvent.Server;
            listing.Location = calendarEvent.Location;
            listing.Rating = calendarEvent.Rating;
            listing.LinkTexts = calendarEvent.LinkTexts;
            listing.Openings = calendarEvent.Openings;
            listing.LastValidated = calendarEvent.LastValidated;
        }
    }

    private static (RowData Row, ListingOpening Opening)? Read(JsonElement element)
    {
        RowData? row;
        try
        {
            row = element.Deserialize<RowData>();
        }
        catch (JsonException)
        {
            return null;
        }

        if (row is null || string.IsNullOrWhiteSpace(row.UId)
            || row.Start is not { } start || row.End is not { } end || end <= start)
            return null;

        return (row, new ListingOpening(start, end));
    }

    /// <summary>The day part of the calendar's validation time ("2026-08-30T00:00:00"), or null if missing or unreadable.</summary>
    internal static DateOnly? LastValidated(string? written) =>
        DateTime.TryParse(written, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? DateOnly.FromDateTime(time)
            : null;

    // The API's shape, only the fields Jaunt reads. Others, such as the description and contacts,
    // which hold Discord handles, are ignored.
    private sealed class RowData
    {
        [JsonPropertyName("uId")]
        public string? UId { get; set; }

        [JsonPropertyName("eventName")]
        public string? EventName { get; set; }

        [JsonPropertyName("server")]
        public string? Server { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("eventURL")]
        public string? EventUrl { get; set; }

        [JsonPropertyName("startTimeUTC")]
        public DateTimeOffset? Start { get; set; }

        [JsonPropertyName("endTimeUTC")]
        public DateTimeOffset? End { get; set; }

        [JsonPropertyName("isRecurring")]
        public bool? IsRecurring { get; set; }

        [JsonPropertyName("lastValidated")]
        public string? LastValidated { get; set; }

        [JsonPropertyName("esrbRating")]
        public string? Rating { get; set; }
    }
}
