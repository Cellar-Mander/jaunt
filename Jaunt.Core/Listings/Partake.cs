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

    /// <summary>One event as Jaunt keeps it.</summary>
    /// <param name="World">The world it is on, corrected to the game's spelling where the gazetteer knows it.</param>
    /// <param name="DataCentre">The data centre Partake gives, or null.</param>
    /// <param name="Location">The free-text location, for the address parser; empty when none is given.</param>
    /// <param name="Links">The host's website and Discord, http or https only.</param>
    public sealed record Event(
        string Id, string Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string World, string? DataCentre,
        string Location, IReadOnlyList<Uri> Links);

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

    /// <summary>Reads one response.</summary>
    /// <param name="gazetteer">Worlds, to correct a misspelt world name.</param>
    /// <exception cref="JsonException">The response has GraphQL errors, or no list of events.</exception>
    public static Page Parse(string json, Gazetteer gazetteer)
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
            if (Read(element, gazetteer) is { } item)
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
            listing.Links = item.Links;
        }
    }

    /// <summary>Whether a cached Partake event is upcoming or under way at <paramref name="now"/>.</summary>
    public static bool NotEnded(CachedListing listing, DateTimeOffset now) => listing.EndsAt is { } end && end > now;

    private static Event? Read(JsonElement element, Gazetteer gazetteer)
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
            || data.Server?.Name is not { } server || string.IsNullOrWhiteSpace(server))
            return null;

        var title = ListingDetails.Shorten((data.Title ?? "").Trim());
        if (title.Length == 0)
            return null;

        var dataCentre = data.DataCenter?.Name?.Trim();
        return new Event(
            id.ToString(CultureInfo.InvariantCulture),
            title,
            start,
            end,
            FfxivVenues.World(server.Trim(), gazetteer),
            string.IsNullOrEmpty(dataCentre) ? null : dataCentre,
            (data.Location ?? "").Trim(),
            FfxivVenues.Links(data.Team?.WebsiteUrl, data.Team?.DiscordUrl));
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
