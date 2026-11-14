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

    public const string Game = "final-fantasy-xiv";

    public const int PageSize = 200;

    /// <summary>Bounds one fetch to <see cref="PageSize"/> × this many events.</summary>
    public const int MaxPages = 5;

    /// <summary>How far back to ask for start times, to include events still under way.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromHours(24);

    // Only the fields Jaunt shows, as Partake's acceptable-use rules ask
    private const string Query = """
        query Events($game: String, $start: DateTime, $limit: Int, $offset: Int) {
          events(game: $game, startsBetween: { start: $start }, sortBy: STARTS_AT_ASC, limit: $limit, offset: $offset) {
            id title startsAt endsAt location
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
    public sealed record Event(
        string Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string World, string? DataCentre,
        string? Location, IReadOnlyList<string> LinkTexts);

    /// <summary>What the list shows for a cached event, worked out from the values Partake sent.</summary>
    /// <param name="World">Corrected to the game's spelling where the gazetteer knows it.</param>
    /// <param name="DataCentre">Trimmed; null when blank.</param>
    /// <param name="Location">Trimmed; empty when none.</param>
    /// <param name="Links">The website and Discord, http or https only.</param>
    public sealed record Shown(string Title, string World, string? DataCentre, string Location, IReadOnlyList<Uri> Links);

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
    public static void Record(ListingCache cache, IEnumerable<Event> events, DateTimeOffset now)
    {
        foreach (var item in events)
        {
            if (item.EndsAt <= now)
                continue;

            var listing = cache.Seen(ListingSource.Partake, item.Id, item.Title, now, oneOff: true);
            listing.StartsAt = item.StartsAt;
            listing.EndsAt = item.EndsAt;
            listing.ListedOn = item.World;
            listing.DataCentre = item.DataCentre;
            listing.Location = item.Location;
            listing.LinkTexts = item.LinkTexts;
        }
    }

    /// <summary>The title, world, data centre, location and links to show for a cached event.</summary>
    /// <param name="gazetteer">Worlds, to correct a misspelt world name.</param>
    public static Shown Show(CachedListing listing, Gazetteer gazetteer)
    {
        var dataCentre = listing.DataCentre?.Trim();
        return new Shown(
            Title(listing.Title),
            FfxivVenues.World((listing.ListedOn ?? "").Trim(), gazetteer),
            string.IsNullOrEmpty(dataCentre) ? null : dataCentre,
            (listing.Location ?? "").Trim(),
            FfxivVenues.Links([.. listing.LinkTexts]));
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
        return new Event(id.ToString(CultureInfo.InvariantCulture), title, start, end, server, data.DataCenter?.Name, data.Location, links);
    }

    // The API's shapes, only the fields Jaunt asks for
    private sealed class EventData
    {
        [JsonPropertyName("id")]
        public long? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

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
