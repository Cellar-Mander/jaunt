namespace Jaunt.Core.Listings;

/// <summary>
/// Listings seen in any source, kept between sessions so the browser can show them while a source
/// is cooled down, and so favourite, visited and hidden marks survive. Also keeps when each source
/// (or each data centre, for a source fetched by data centre) was last fetched, for
/// <see cref="RefreshPolicy"/>; clearing listings never resets those times.
/// </summary>
/// <remarks>
/// Unmarked listings expire one month after they were last seen (<see cref="RemoveExpired"/>);
/// marked ones are kept. Lookups are by source and ID, so recording a listing costs the same however
/// many are cached. It remembers which sources, marks and fetch times changed, so a save writes only
/// those files (<see cref="TakeChanges"/>). Not thread-safe: use from one thread.
/// </remarks>
public sealed partial class ListingCache
{
    private readonly Dictionary<ListingSource, Dictionary<string, CachedListing>> listings = [];
    // Keyed by source and data centre: "" for a source fetched for every region at once (RefreshPolicy.ByDataCentre)
    private readonly Dictionary<(ListingSource Source, string DataCentre), DateTimeOffset> lastFetched = [];
    private readonly HashSet<ListingSource> changedSources = [];
    private bool marksChanged;
    private bool fetchTimesChanged;

    public int Count => listings.Values.Sum(bySource => bySource.Count);

    public IEnumerable<CachedListing> Listings => listings.Values.SelectMany(bySource => bySource.Values);

    /// <summary>Records that a source listed this listing now, adding it if it is new.</summary>
    /// <param name="oneOff">Whether it is a single event (<see cref="CachedListing.OneOff"/>), as the source last said.</param>
    public CachedListing Seen(ListingSource source, string id, string title, DateTimeOffset now, bool oneOff = false)
    {
        var listing = Touch(source, id, now);
        // The marks file keeps titles and one-off flags, for the hidden list when the cache is lost
        if (listing.IsMarked && (listing.OneOff != oneOff || !string.Equals(listing.Title, title, StringComparison.Ordinal)))
            marksChanged = true;
        listing.Title = title;
        listing.OneOff = oneOff;
        return listing;
    }

    /// <summary>
    /// Records that a source listed this Party Finder ad now, the same way for every source that
    /// carries PF ads: titled by <see cref="ListingDetails.Title"/>, with its text kept for the parsers.
    /// </summary>
    /// <param name="adText">The ad's text as the game sends it, glyphs included.</param>
    /// <param name="listedOn">The world the ad was put up on, or null if the source doesn't say.</param>
    public CachedListing SeenAd(ListingSource source, string id, string adText, string? listedOn, DateTimeOffset now)
    {
        var listing = Touch(source, id, now);
        // New text is a new title, which the marks file keeps
        if (listing.IsMarked && (listing.OneOff || !string.Equals(listing.Description, adText, StringComparison.Ordinal)))
            marksChanged = true;
        listing.OneOff = false;
        listing.Description = adText;
        listing.ListedOn = listedOn;
        return listing;
    }

    /// <summary>Records that another source listed this cached listing now, without changing its fields.</summary>
    public void SeenElsewhere(CachedListing listing, DateTimeOffset now)
    {
        if (now <= listing.LastSeen)
            return;
        listing.LastSeen = now;
        changedSources.Add(listing.Source);
    }

    private CachedListing Touch(ListingSource source, string id, DateTimeOffset now)
    {
        var bySource = Of(source);
        if (!bySource.TryGetValue(id, out var listing))
        {
            listing = new CachedListing { Source = source, Id = id, Owner = this };
            bySource.Add(id, listing);
        }

        listing.Recorded = true;
        // Stryker disable once Equality : when now equals LastSeen, assigning it changes nothing
        if (now > listing.LastSeen)
            listing.LastSeen = now;
        changedSources.Add(source);
        return listing;
    }

    private Dictionary<string, CachedListing> Of(ListingSource source)
    {
        if (!listings.TryGetValue(source, out var bySource))
        {
            bySource = new Dictionary<string, CachedListing>(StringComparer.Ordinal);
            listings.Add(source, bySource);
        }

        return bySource;
    }

    public CachedListing? Find(ListingSource source, string id) =>
        listings.TryGetValue(source, out var bySource) ? bySource.GetValueOrDefault(id) : null;

    /// <summary>Hidden listings, by title as shown.</summary>
    public IReadOnlyList<CachedListing> HiddenListings() =>
        [.. Listings.Where(l => l.Hidden).OrderBy(l => l.ShownTitle, StringComparer.CurrentCultureIgnoreCase)];

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
        var removed = 0;
        foreach (var (source, bySource) in listings)
        {
            var doomed = bySource.Values.Where(predicate).ToList();
            foreach (var listing in doomed)
            {
                bySource.Remove(listing.Id);
                listing.Owner = null;
                marksChanged |= listing.IsMarked;
            }

            if (doomed.Count > 0)
                changedSources.Add(source);
            removed += doomed.Count;
        }

        return removed;
    }

    /// <summary>When the source was last fetched, for the data centre if it is fetched by data centre, or null if never.</summary>
    /// <param name="dataCentre">The data centre for a source fetched by data centre (<see cref="RefreshPolicy.ByDataCentre"/>), else null.</param>
    public DateTimeOffset? LastFetched(ListingSource source, string? dataCentre = null) =>
        lastFetched.TryGetValue((source, dataCentre ?? ""), out var time) ? time : null;

    /// <summary>
    /// Records a request to the source, whether or not it succeeded: a failed request still counts
    /// against the source's rate limit.
    /// </summary>
    /// <param name="dataCentre">The data centre asked for, for a source fetched by data centre; else null.</param>
    /// <exception cref="ArgumentException">
    /// The in-game Party Finder, which is never fetched, or a data centre given for a source fetched
    /// for every region, or missing for one fetched by data centre.
    /// </exception>
    public void Fetched(ListingSource source, DateTimeOffset now, string? dataCentre = null)
    {
        if (RefreshPolicy.Minimum(source) is null)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new ArgumentException($"{source} is never fetched", nameof(source));
        if (RefreshPolicy.ByDataCentre(source) == string.IsNullOrEmpty(dataCentre))
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new ArgumentException($"{source} is fetched {(RefreshPolicy.ByDataCentre(source) ? "by data centre" : "for every region")}", nameof(dataCentre));
        lastFetched[(source, dataCentre ?? "")] = now;
        fetchTimesChanged = true;
    }

    /// <summary>Whether opening the browser or Refresh should fetch the source (for the data centre) now (<see cref="RefreshPolicy.ShouldFetch"/>).</summary>
    public bool ShouldFetch(ListingSource source, int settingMinutes, DateTimeOffset now, string? dataCentre = null) =>
        RefreshPolicy.ShouldFetch(source, settingMinutes, LastFetched(source, dataCentre), now);

    internal void MarksChanged() => marksChanged = true;
}
