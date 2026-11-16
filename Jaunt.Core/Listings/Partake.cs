using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Reads FFXIV events from Partake's public GraphQL API (<see cref="Url"/>) and records them in the
/// listing cache. The rules are in <c>docs/design.md</c> under "Partake".
/// </summary>
/// <remarks>
/// The cache keeps each event's values as Partake sent them; <see cref="Show"/> works out the title,
/// world and links when they are shown, so a change to those rules applies without a new fetch.
/// A fetch asks for events starting from <see cref="Lookback"/> before now, so events already under
/// way are included, one page of <see cref="PageSize"/> at a time until a page comes back short, at
/// most <see cref="MaxPages"/> pages. About 150 events are listed at once, so one page is usual.
/// </remarks>
public static class Partake
{
    public const string Url = "https://api.partake.gg/";

    /// <summary>Partake's site, linked from Settings' list of sources.</summary>
    public static readonly Uri SiteUrl = new("https://partake.gg/");

    /// <summary>Where Partake keeps event images, by file name.</summary>
    private const string AssetsUrl = "https://cdn.partake.gg/assets/";

    /// <summary>Partake's API terms bar apps from implying endorsement, so this shows beside its events.</summary>
    public const string Disclaimer = "Jaunt is not affiliated with or endorsed by Partake.";

    public const string Game = "final-fantasy-xiv";

    public const int PageSize = 200;

    /// <summary>Bounds one fetch to <see cref="PageSize"/> × this many events.</summary>
    public const int MaxPages = 5;

    /// <summary>How far back to ask for start times, to include events still under way.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromHours(24);

    // Only the fields Jaunt shows or filters on, as Partake's acceptable-use rules ask
    private const string Query = """
        query Events($game: String, $start: DateTime, $limit: Int, $offset: Int) {
          events(game: $game, startsBetween: { start: $start }, sortBy: STARTS_AT_ASC, limit: $limit, offset: $offset) {
            id title description attachments startsAt endsAt location ageRating tags
            server { name } dataCenter { name }
            team { websiteUrl discordUrl }
          }
        }
        """;

    /// <summary>One event as Partake sent it, after the checks that decide whether it is kept.</summary>
    /// <param name="Title">As sent; it has a character other than whitespace.</param>
    /// <param name="World">The world (<c>server.name</c>) as sent; it has a character other than whitespace.</param>
    /// <param name="DataCentre">As sent, or null.</param>
    /// <param name="Location">The free-text location as sent, or null.</param>
    /// <param name="LinkTexts">The host's website and Discord as sent, leaving out missing ones.</param>
    /// <param name="AgeRating">As sent ("EVERYONE", "MATURE", "ADULT"), or null.</param>
    /// <param name="Tags">The event's tags as sent ("Live DJs"), leaving out missing ones.</param>
    /// <param name="About">Its description, cleaned when read; null if it has none.</param>
    /// <param name="Attachments">Its images' file names on Partake's CDN as sent ("a21b….webp"), leaving out missing ones; null if none.</param>
    /// <param name="DescriptionImages">The addresses of the images its description shows, as written, in order; null if none.</param>
    public sealed record Event(
        string Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string World, string? DataCentre,
        string? Location, IReadOnlyList<string> LinkTexts, string? AgeRating = null, IReadOnlyList<string>? Tags = null,
        ListingAbout? About = null, IReadOnlyList<string>? Attachments = null, IReadOnlyList<string>? DescriptionImages = null);

    /// <summary>What the list shows for a cached event, worked out from the values Partake sent.</summary>
    /// <param name="World">Corrected to the game's spelling where the gazetteer knows it.</param>
    /// <param name="Location">Trimmed; empty when none.</param>
    /// <param name="Links">The website and Discord, then the Discord invites its description links to, http or https only.</param>
    public sealed record Shown(string Title, string World, string Location, IReadOnlyList<Uri> Links);

    /// <summary>One response: the events kept, and how many the page held before any were skipped.</summary>
    public sealed record Page(IReadOnlyList<Event> Events, int Received);

    /// <summary>The POST body asking for one page of events starting at or after <paramref name="start"/>.</summary>
    public static string RequestBody(DateTimeOffset start, int offset) => JsonSerializer.Serialize(new
    {
        query = Query,
        variables = new
        {
            game = Game,
            start = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            limit = PageSize,
            offset,
        },
    });

    /// <summary>Whether another page should be asked for after this one.</summary>
    /// <param name="pagesRead">Pages read so far, this one included.</param>
    public static bool HasMore(Page page, int pagesRead) => page.Received >= PageSize && pagesRead < MaxPages;

    /// <summary>Reads one response, keeping each event's values as sent.</summary>
    /// <exception cref="JsonException">The response has GraphQL errors, or no list of events.</exception>
    public static Page Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException("Partake did not send a GraphQL response");

        // GraphQL reports errors with HTTP 200, so they must be looked for in the body
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException($"Partake reported errors: {errors.GetRawText()}");

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("events", out var list) || list.ValueKind != JsonValueKind.Array)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException("Partake did not send a list of events");

        var events = new List<Event>();
        foreach (var element in list.EnumerateArray())
        {
            if (Read(element) is { } item)
                events.Add(item);
        }

        return new Page(events, list.GetArrayLength());
    }

    /// <summary>Records that Partake listed these events now, leaving out any that have already ended.</summary>
    /// <param name="region">
    /// The worlds the character can travel to; an event whose world and data centre are both outside it
    /// is left out. Null keeps every event, as when loading the cache.
    /// </param>
    public static void Record(ListingCache cache, IEnumerable<Event> events, DateTimeOffset now, TravelRegion? region = null)
    {
        foreach (var item in events)
        {
            if (item.EndsAt <= now
                || (region is not null && !region.IncludesWorld(item.World) && !region.IncludesDataCentre(item.DataCentre)))
                continue;

            var listing = cache.Seen(ListingSource.Partake, item.Id, item.Title, now, oneOff: true);
            listing.StartsAt = item.StartsAt;
            listing.EndsAt = item.EndsAt;
            listing.ListedOn = item.World;
            listing.DataCentre = item.DataCentre;
            listing.Location = item.Location;
            listing.LinkTexts = item.LinkTexts;
            listing.Rating = item.AgeRating;
            listing.SourceTags = item.Tags ?? [];
            listing.About = item.About;
            listing.Attachments = item.Attachments ?? [];
            listing.DescriptionImages = item.DescriptionImages ?? [];
        }
    }

    /// <summary>The title, world, location and links to show for a cached event.</summary>
    /// <param name="gazetteer">Worlds, to correct a misspelt world name.</param>
    public static Shown Show(CachedListing listing, Gazetteer gazetteer) => new(
        Title(listing.Title),
        FfxivVenues.World((listing.ListedOn ?? "").Trim(), gazetteer),
        (listing.Location ?? "").Trim(),
        FfxivVenues.Links([.. listing.LinkTexts, .. listing.About?.Invites ?? []]));

    /// <summary>
    /// The housing addresses in an event's location, in the order written. One that names no world is
    /// on the event's world, which Partake gives as data, so that world counts as written.
    /// </summary>
    public static IReadOnlyList<HousingAddress> Addresses(string location, string world, AddressParser parser) =>
        [.. parser.Parse(location, world).Select(a => a.WorldFrom == WorldSource.Listing ? a with { WorldFrom = WorldSource.Text } : a)];

    /// <summary>
    /// The address of an event image on Partake's CDN, from its file name as sent. Names sent without
    /// an extension are WebP files there ("205b…" is at "205b….webp"). Null for a name that isn't a plain
    /// file name of letters, digits, "-", "_" and ".".
    /// </summary>
    public static Uri? AttachmentUri(string name)
    {
        if (name.Length is 0 or > 100 || name.StartsWith('.') || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            return null;
        return new Uri(AssetsUrl + (name.Contains('.', StringComparison.Ordinal) ? name : name + ".webp"));
    }

    /// <summary>An event's title as shown: trimmed and cut to 60 characters like an ad's.</summary>
    public static string Title(string sent) => ListingDetails.Shorten(sent.Trim());

    /// <summary>Whether a cached Partake event is upcoming or under way at <paramref name="now"/>.</summary>
    public static bool NotEnded(CachedListing listing, DateTimeOffset now) => listing.EndsAt is { } end && end > now;

    private static Event? Read(JsonElement element)
    {
        EventData? data;
        try
        {
            data = element.Deserialize<EventData>();
        }
        catch (JsonException)
        {
            return null;
        }

        // An event without a world can't be travelled to, and one ending before it starts has a wrong time
        if (data?.Id is not { } id || data.StartsAt is not { } start || data.EndsAt is not { } end || end <= start
            || data.Server?.Name is not { } server || string.IsNullOrWhiteSpace(server)
            || data.Title is not { } title || string.IsNullOrWhiteSpace(title))
            return null;

        string[] links = [.. new[] { data.Team?.WebsiteUrl, data.Team?.DiscordUrl }.OfType<string>()];
        return new Event(
            id.ToString(CultureInfo.InvariantCulture), title, start, end, server, data.DataCenter?.Name, data.Location, links,
            data.AgeRating, [.. (data.Tags ?? []).OfType<string>()], ListingAbout.Clean(data.Description),
            [.. (data.Attachments ?? []).OfType<string>()], ListingAbout.Images(data.Description));
    }

    // The API's shapes, only the fields Jaunt asks for
    private sealed class EventData
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>Markdown, often with images.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("attachments")]
        public List<string?>? Attachments { get; set; }

        [JsonPropertyName("startsAt")]
        public DateTimeOffset? StartsAt { get; set; }

        [JsonPropertyName("endsAt")]
        public DateTimeOffset? EndsAt { get; set; }

        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("server")]
        public NameData? Server { get; set; }

        [JsonPropertyName("dataCenter")]
        public NameData? DataCenter { get; set; }

        [JsonPropertyName("team")]
        public TeamData? Team { get; set; }

        [JsonPropertyName("ageRating")]
        public string? AgeRating { get; set; }

        [JsonPropertyName("tags")]
        public List<string?>? Tags { get; set; }
    }

    private sealed class NameData
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed class TeamData
    {
        [JsonPropertyName("websiteUrl")]
        public string? WebsiteUrl { get; set; }

        [JsonPropertyName("discordUrl")]
        public string? DiscordUrl { get; set; }
    }
}
