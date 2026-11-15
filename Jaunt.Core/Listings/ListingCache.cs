using Jaunt.Core.Browser;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Listings seen in any source, kept between sessions so the browser can show them while a source
/// is cooled down, and so favourite, visited and hidden marks survive. Also keeps when each source
/// (or each data centre, for a source fetched by data centre) was last fetched, for
/// <see cref="RefreshPolicy"/>; clearing listings never resets those times.
/// </summary>
/// <remarks>
/// Unmarked listings expire one month after they were last seen, or for Party Finder ads once they
/// are no longer listed and state no upcoming time (<see cref="RemoveExpired"/>); marked ones are kept. Lookups are by source and ID, so recording a listing costs the same however
/// many are cached. It remembers which sources, marks and fetch times changed, so a save writes only
/// those files (<see cref="TakeChanges"/>). Not thread-safe: use from one thread.
/// </remarks>
public sealed partial class ListingCache
{
    private readonly Dictionary<ListingSource, Dictionary<string, CachedListing>> listings = [];
    // Keyed by source and data centre: "" for a source fetched for every region at once (RefreshPolicy.ByDataCentre)
    private readonly Dictionary<(ListingSource Source, string DataCentre), DateTimeOffset> lastFetched = [];
    private readonly HashSet<ListingSource> changedSources = [];
    // Marks on housing addresses; one with no mark left is dropped at the next save
    private readonly Dictionary<AddressKey, PlaceMark> places = [];
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
        // New text starts a new first sighting; the same text seen earlier moves it back
        if (listing.FirstSeen is not { } first || now < first || !string.Equals(listing.Description, adText, StringComparison.Ordinal))
            listing.FirstSeen = now;
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

    /// <summary>The addresses with a mark (<see cref="PlaceMark"/>).</summary>
    public IEnumerable<PlaceMark> Places => places.Values.Where(p => p.IsMarked);

    /// <summary>The marked place at an address, or null.</summary>
    public PlaceMark? PlaceAt(AddressKey address) => places.TryGetValue(address, out var place) && place.IsMarked ? place : null;

    /// <summary>Hidden places, by title as last shown.</summary>
    public IReadOnlyList<PlaceMark> HiddenPlaces() =>
        [.. Places.Where(p => p.Hidden).OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The place at an address, added with no marks if there is none; marks set on it are saved.</summary>
    public PlaceMark Place(AddressKey address)
    {
        if (!places.TryGetValue(address, out var place))
        {
            place = new PlaceMark(address) { Owner = this };
            places.Add(address, place);
        }

        return place;
    }

    /// <summary>
    /// Moves a place's marks to another address. If that address has marks already, the two become
    /// one there, with every mark and everything seen at either.
    /// </summary>
    /// <returns>The place now at <paramref name="to"/>.</returns>
    internal PlaceMark Move(PlaceMark place, AddressKey to)
    {
        if (place.Address == to)
            return place;
        places.Remove(place.Address);
        marksChanged = true;
        if (!places.TryGetValue(to, out var there))
        {
            place.Address = to;
            places.Add(to, place);
            return place;
        }

        there.Favourite |= place.Favourite;
        there.Visited |= place.Visited;
        there.Hidden |= place.Hidden;
        there.VenueIds.UnionWith(place.VenueIds);
        there.Websites.UnionWith(place.Websites);
        there.Discords.UnionWith(place.Discords);
        // Its address now names where its marks went, for anything still holding it
        place.Address = to;
        place.Owner = null;
        return there;
    }

    /// <summary>Removes every listing that is not favourite, visited or hidden.</summary>
    /// <returns>How many were removed.</returns>
    public int ClearUnmarked() => RemoveWhere(l => !l.IsMarked);

    /// <summary>
    /// How long an unmarked Party Finder ad is kept after it was last seen, and how far before now a
    /// time it states may end and still keep it. It covers the time the ad may still count as listed
    /// (<see cref="Schedules.ListedUntil"/>: up to 25 hours at the longest cooldown) and how far the
    /// zone its times are read in may shift them from London's, which depends on the player's region
    /// and the venues at its address (up to 14 hours).
    /// </summary>
    public static readonly TimeSpan AdGrace = TimeSpan.FromDays(2);

    /// <summary>
    /// Removes unmarked Party Finder ads last seen <see cref="AdGrace"/> or more before
    /// <paramref name="now"/> whose text states no time ending (or, with no end, starting) after
    /// <paramref name="now"/> less <see cref="AdGrace"/>, and other listings last seen a month or more
    /// before it. Marked listings are kept unless they are one-off.
    /// </summary>
    /// <returns>How many were removed.</returns>
    public int RemoveExpired(DateTimeOffset now) =>
        RemoveWhere(l => l.Source is ListingSource.InGamePartyFinder or ListingSource.Xivpf && !l.IsMarked
            ? AdExpired(l, now)
            : (l.OneOff || !l.IsMarked) && l.LastSeen.AddMonths(1) <= now);

    private static bool AdExpired(CachedListing ad, DateTimeOffset now)
    {
        var since = now - AdGrace;
        if (ad.LastSeen > since)
            return false;
        // Read as no longer listed; the grace covers any zone the browser might read its times in
        var schedule = Schedules.ForAd(Schedules.ParseAd(ad.Description, ad.LastSeen), ad.LastSeen, PartyFinderRegion.Europe,
            listed: false, since, firstSeen: ad.FirstSeen);
        return !schedule.Occurrences.Any(o => (o.End ?? o.Start) > since);
    }

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

    /// <summary>Whether a mark changed since the last save, so a caller that changes them without a click knows to save.</summary>
    public bool MarksUnsaved => marksChanged;
}
