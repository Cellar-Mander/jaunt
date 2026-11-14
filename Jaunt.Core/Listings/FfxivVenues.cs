using System.Globalization;
using System.Text.Json;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Reads the venue list from FFXIV Venues (<see cref="Url"/>), records each venue's fields as sent in
/// the listing cache, and derives what the list shows from those fields (<see cref="Show"/>). The
/// rules are in <c>docs/design.md</c> under "FFXIV Venues".
/// </summary>
/// <remarks>
/// The response is every approved venue opening in the coming week at once, about 1.6 MB for 750
/// venues. Reading it is one pass over the venues; a venue that doesn't have the expected shape is skipped rather than failing the rest.
/// The cache keeps the name, location, links and schedule as sent, so a change to how they are read
/// applies to cached venues without fetching again.
/// </remarks>
public static class FfxivVenues
{
    /// <summary>
    /// Approved venues (already the API's default, pinned here) that open within the coming week, as the
    /// API works it out. A venue left out keeps its cached copy and marks until it expires.
    /// </summary>
    public const string Url = "https://api.ffxivvenues.com/v1.0/venue?approved=true&withinWeek=true";

    /// <summary>
    /// The shortest opening that counts as a whole day. Venues open around the clock enter 00:00 to
    /// 23:59, as the site's schedule form can't end a day at 24:00.
    /// </summary>
    public static readonly TimeSpan WholeDay = new(23, 59, 0);

    /// <summary>A misspelt world ("Bismark") must share at least this many letters with the real one.</summary>
    private const int MinWorldPrefix = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, District> Districts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mist"] = District.Mist,
        ["Lavender Beds"] = District.LavenderBeds,
        ["The Lavender Beds"] = District.LavenderBeds,
        ["Goblet"] = District.Goblet,
        ["The Goblet"] = District.Goblet,
        ["Shirogane"] = District.Shirogane,
        ["Empyreum"] = District.Empyreum,
    };

    /// <summary>One venue as the API sends it, with only the fields Jaunt reads.</summary>
    /// <param name="Name">Its name as sent; null if missing.</param>
    public sealed record Venue(string Id, string? Name, Fields Fields);

    /// <summary>A venue's fields as the API sends them, other than its ID and name.</summary>
    /// <param name="Location">Null if missing.</param>
    /// <param name="Schedule">Its weekly openings; null if missing.</param>
    public sealed record Fields(Location? Location, string? Website, string? Discord, IReadOnlyList<Opening?>? Schedule);

    /// <summary>What the list shows for a venue, derived from its cached fields.</summary>
    /// <param name="Address">Null when the venue gives no housing address (a free-text location, or none).</param>
    /// <param name="Links">Its website and Discord, http or https only.</param>
    public sealed record Shown(string Title, HousingAddress? Address, IReadOnlyList<Uri> Links, bool AlwaysOpen);

    /// <summary>Reads the API's response.</summary>
    /// <exception cref="JsonException">The text is not a JSON array.</exception>
    public static IReadOnlyList<Venue> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException("FFXIV Venues did not send a list of venues");

        var venues = new List<Venue>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (Read(element) is { } venue)
                venues.Add(venue);
        }

        return venues;
    }

    /// <summary>
    /// Records that FFXIV Venues listed these venues now, keeping the name as the listing's title and
    /// the other fields, all as sent.
    /// </summary>
    public static void Record(ListingCache cache, IEnumerable<Venue> venues, DateTimeOffset now)
    {
        foreach (var venue in venues)
            cache.Seen(ListingSource.FfxivVenues, venue.Id, venue.Name ?? "", now).Venue = venue.Fields;
    }

    /// <summary>What the list shows for a cached venue, derived from its fields as sent.</summary>
    /// <param name="gazetteer">Worlds, to correct a misspelt world name.</param>
    public static Shown Show(CachedListing listing, Gazetteer gazetteer)
    {
        var fields = listing.Venue;
        return new Shown(
            Title(listing.Title),
            Address(fields?.Location, gazetteer),
            Links(fields?.Website, fields?.Discord),
            AlwaysOpen(fields?.Schedule));
    }

    private static Venue? Read(JsonElement element)
    {
        VenueData? data;
        try
        {
            data = element.Deserialize<VenueData>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (data is null || string.IsNullOrWhiteSpace(data.Id) || data.Approved == false)
            return null;

        return new Venue(data.Id, data.Name, new Fields(data.Location, data.Website, data.Discord, data.Schedule));
    }

    /// <summary>The title shown for a venue's name as sent: first line only, trimmed and cut to 60 characters.</summary>
    public static string Title(string name)
    {
        var text = name.Trim();
        var lineEnd = text.IndexOfAny(['\r', '\n']);
        // Stryker disable once Equality : the name is trimmed, so it never starts with a line break and lineEnd is never 0
        return ListingDetails.Shorten(lineEnd < 0 ? text : text[..lineEnd].TrimEnd());
    }

    /// <summary>
    /// The venue's housing address: a known district and a ward of 1 to 30, with a plot of 1 to 60 or
    /// else an apartment. Null otherwise, such as for a venue that gives only a free-text location.
    /// </summary>
    internal static HousingAddress? Address(Location? location, Gazetteer gazetteer)
    {
        if (location?.World is not { } written || string.IsNullOrWhiteSpace(written)
            || location.District is null || !Districts.TryGetValue(location.District.Trim(), out var district)
            || location.Ward is < 1 or > 30)
            return null;

        int? plot = location.Plot is >= 1 and <= 60 ? location.Plot : null;
        int? apartment = plot is null && location.Apartment > 0 ? location.Apartment : null;
        var room = location.Room > 0 ? location.Room.ToString(CultureInfo.InvariantCulture) : null;
        return new HousingAddress(World(written.Trim(), gazetteer), WorldSource.Text, district, location.Ward, plot, location.Subdivision, apartment, room);
    }

    /// <summary>
    /// The world's name as the game writes it. A misspelling is corrected to the one world sharing
    /// its longest beginning of at least <see cref="MinWorldPrefix"/> letters; an unknown name is kept as written.
    /// </summary>
    internal static string World(string written, Gazetteer gazetteer)
    {
        if (gazetteer.FindWorld(written) is { } world)
            return world.Name;

        for (var length = written.Length - 1; length >= MinWorldPrefix; length--)
        {
            if (gazetteer.WorldByPrefix(written[..length], region: null) is { } match)
                return match.Name;
        }

        return written;
    }

    /// <summary>The website and Discord links, in that order, keeping only http and https ones.</summary>
    internal static IReadOnlyList<Uri> Links(params string?[] written)
    {
        var links = new List<Uri>();
        foreach (var text in written)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;
            // Venues sometimes leave out the scheme ("discord.gg/abc"), which WebLinks reads as https
            var uri = Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed) && WebLinks.IsWeb(parsed)
                ? parsed
                : WebLinks.Find(text).FirstOrDefault()?.Uri;
            if (uri is not null && !links.Contains(uri))
                links.Add(uri);
        }

        return links;
    }

    /// <summary>
    /// Whether the schedule opens every day of every week for at least <see cref="WholeDay"/>. A venue
    /// with a long opening on only some days, or every other week, is not open around the clock.
    /// </summary>
    internal static bool AlwaysOpen(IEnumerable<Opening?>? schedule)
    {
        var days = new HashSet<int>();
        foreach (var opening in schedule ?? [])
        {
            if (opening?.Start is { } start && opening.End is { } end
                && opening.Interval is { IntervalType: 0, IntervalArgument: 1 }
                && opening.Day is >= 0 and <= 6
                && Length(start, end) >= WholeDay)
                days.Add(opening.Day);
        }

        return days.Count == 7;
    }

    private static TimeSpan Length(Time start, Time end) =>
        new TimeSpan(end.NextDay ? 1 : 0, end.Hour, end.Minute, 0) - new TimeSpan(start.Hour, start.Minute, 0);

    // The API's shape, only the fields Jaunt reads. Others, such as managers' Discord IDs, are ignored.
    private sealed class VenueData
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Location? Location { get; set; }
        public string? Website { get; set; }
        public string? Discord { get; set; }
        public bool? Approved { get; set; }
        public List<Opening?>? Schedule { get; set; }
    }

    /// <summary>A venue's location as the API sends it. A venue that gives only a free-text location sends no world or district.</summary>
    public sealed record Location(string? World, string? District, int Ward, int Plot, int Apartment, int Room, bool Subdivision);

    /// <param name="Day">0 is Monday.</param>
    /// <param name="End">Null means the API's default of three hours after the start.</param>
    public sealed record Opening(int Day, Time? Start, Time? End, Interval? Interval);

    public sealed record Time(int Hour, int Minute, bool NextDay);

    /// <summary>Type 0 is every <c>IntervalArgument</c> weeks; type 1 is a week of each month.</summary>
    public sealed record Interval(int IntervalType, int IntervalArgument);
}
