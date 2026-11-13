using System.Text.Encodings.Web;
using System.Text.Json;

namespace Jaunt.Core.Listings;

/// <summary>
/// Listings seen in any source, kept between sessions so the browser can show them while a source
/// is cooled down, and so favourite, visited and hidden marks survive. Also keeps when each source
/// was last fetched, for <see cref="RefreshPolicy"/>; clearing listings never resets those times.
/// </summary>
/// <remarks>
/// Unmarked listings expire one month after they were last seen (<see cref="RemoveExpired"/>);
/// marked ones are kept. Lookups are by source and ID, so recording a listing costs the same however
/// many are cached. Not thread-safe: use from one thread, and save a <see cref="ToJson"/> snapshot.
/// </remarks>
public sealed class ListingCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<(ListingSource Source, string Id), CachedListing> listings = [];
    private readonly Dictionary<ListingSource, DateTimeOffset> lastFetched = [];

    public int Count => listings.Count;

    public IEnumerable<CachedListing> Listings => listings.Values;

    /// <summary>Records that a source listed this listing now, adding it if it is new.</summary>
    /// <param name="oneOff">Whether it is a single event (<see cref="CachedListing.OneOff"/>), as the source last said.</param>
    public CachedListing Seen(ListingSource source, string id, string title, DateTimeOffset now, bool oneOff = false)
    {
        if (!listings.TryGetValue((source, id), out var listing))
        {
            listing = new CachedListing { Source = source, Id = id };
            listings.Add((source, id), listing);
        }

        listing.Title = title;
        listing.OneOff = oneOff;
        // Stryker disable once Equality : when now equals LastSeen, assigning it changes nothing
        if (now > listing.LastSeen)
            listing.LastSeen = now;
        return listing;
    }

    public CachedListing? Find(ListingSource source, string id) => listings.GetValueOrDefault((source, id));

    /// <summary>Hidden listings, by title.</summary>
    public IReadOnlyList<CachedListing> HiddenListings() =>
        [.. listings.Values.Where(l => l.Hidden).OrderBy(l => l.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>Removes every listing that is not favourite, visited or hidden.</summary>
    /// <returns>How many were removed.</returns>
    public int ClearUnmarked() => RemoveWhere(l => !l.IsMarked);

    /// <summary>
    /// Removes listings last seen a month or more before <paramref name="now"/>, unless they are
    /// marked and recurring.
    /// </summary>
    /// <returns>How many were removed.</returns>
    public int RemoveExpired(DateTimeOffset now) =>
        RemoveWhere(l => (l.OneOff || !l.IsMarked) && l.LastSeen.AddMonths(1) <= now);

    private int RemoveWhere(Func<CachedListing, bool> predicate)
    {
        var doomed = listings.Where(pair => predicate(pair.Value)).Select(pair => pair.Key).ToList();
        foreach (var key in doomed)
            listings.Remove(key);
        return doomed.Count;
    }

    /// <summary>When the source was last fetched, or null if never.</summary>
    public DateTimeOffset? LastFetched(ListingSource source) =>
        lastFetched.TryGetValue(source, out var time) ? time : null;

    /// <summary>
    /// Records a request to the source, whether or not it succeeded: a failed request still counts
    /// against the source's rate limit.
    /// </summary>
    /// <exception cref="ArgumentException">The in-game Party Finder, which is never fetched.</exception>
    public void Fetched(ListingSource source, DateTimeOffset now)
    {
        if (RefreshPolicy.Minimum(source) is null)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new ArgumentException($"{source} is never fetched", nameof(source));
        lastFetched[source] = now;
    }

    /// <summary>Whether opening the browser or Refresh should fetch the source now (<see cref="RefreshPolicy.ShouldFetch"/>).</summary>
    public bool ShouldFetch(ListingSource source, int settingMinutes, DateTimeOffset now) =>
        RefreshPolicy.ShouldFetch(source, settingMinutes, LastFetched(source), now);

    public string ToJson() => JsonSerializer.Serialize(
        new SavedCache
        {
            Listings = [.. listings.Values],
            LastFetched = lastFetched.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
        },
        JsonOptions);

    /// <summary>
    /// Reads a saved cache. Listings without an ID are dropped, and a repeated one keeps the last copy.
    /// Fetch times for sources this version doesn't know, or that are never fetched, are dropped.
    /// </summary>
    /// <exception cref="JsonException">The text is not a saved cache.</exception>
    public static ListingCache FromJson(string json)
    {
        var cache = new ListingCache();
        var saved = JsonSerializer.Deserialize<SavedCache>(json, JsonOptions);
        foreach (var listing in saved?.Listings ?? [])
        {
            if (listing is null || string.IsNullOrEmpty(listing.Id))
                continue;
            listing.Title ??= "";
            cache.listings[(listing.Source, listing.Id)] = listing;
        }

        foreach (var (name, time) in saved?.LastFetched ?? [])
        {
            if (Enum.TryParse<ListingSource>(name, ignoreCase: false, out var source)
                && Enum.IsDefined(source)
                && RefreshPolicy.Minimum(source) is not null)
                cache.lastFetched[source] = time;
        }

        return cache;
    }

    // Sources are saved by name, so the file stays readable and a renumbered source can't take
    // another's fetch time.
    private sealed class SavedCache
    {
        public List<CachedListing?>? Listings { get; set; }
        public Dictionary<string, DateTimeOffset>? LastFetched { get; set; }
    }
}
