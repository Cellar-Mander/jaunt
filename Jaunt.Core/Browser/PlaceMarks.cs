using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>
/// Finds each entry's <see cref="PlaceMark"/> and follows venues that move, starting from the surest
/// evidence: the same FFXIV Venues ID, then the same address, then the same website at a new address
/// when nothing at the old one shows it any more. A Discord invite never moves a mark on its own, as
/// brand and community servers are shared by several venues; nor does a name (decision
/// <c>25e33e0a</c>). The measurements behind this order are in <c>spikes/mark-identity</c>.
/// </summary>
public static class PlaceMarks
{
    /// <summary>
    /// Hosts whose links name a person or one event rather than a venue: DJs' streams (about half the
    /// non-Discord links in the corpus's venue ads are Twitch), social profiles and Partake event pages.
    /// Never kept as a place's website, so a DJ playing at another venue can't move its marks.
    /// </summary>
    private static readonly HashSet<string> NotVenueHosts = new(StringComparer.Ordinal)
    {
        "twitch.tv", "kick.com", "youtube.com", "youtu.be", "x.com", "twitter.com", "instagram.com", "tiktok.com",
        "bsky.app", "soundcloud.com", "mixcloud.com", "partake.gg", "prtk.gg",
    };

    /// <summary>
    /// The place each entry carries the marks of, in the entries' order (null for none), after
    /// moving places whose venue moved and keeping what each matched entry shows there. Marks on
    /// listings that an entry with a plot or apartment holds move to its place first, so marks set
    /// before places existed carry over.
    /// </summary>
    /// <remarks>Each pass is one look-up per entry or link, so the cost grows with the entries, not entries times places.</remarks>
    public static PlaceMark?[] Match(ListingCache cache, IReadOnlyList<MarkEntry> entries)
    {
        var matched = new PlaceMark?[entries.Count];
        var moved = new HashSet<PlaceMark>();
        for (var i = 0; i < entries.Count; i++)
            Adopt(cache, entries[i]);

        // 1. The same FFXIV Venues ID: certain, and where the venue is now, so the place moves there
        var byVenue = new Dictionary<string, PlaceMark>(StringComparer.Ordinal);
        foreach (var place in cache.Places)
        {
            foreach (var id in place.VenueIds)
                byVenue.TryAdd(id, place);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].Places.Count == 0)
                continue;
            foreach (var venue in entries[i].Members.Where(m => m.Source == ListingSource.FfxivVenues))
            {
                if (byVenue.TryGetValue(venue.Id, out var place) && place.Owner is not null)
                {
                    matched[i] = Moved(cache, place, entries[i].Places[0], moved);
                    break;
                }
            }
        }

        // 2. The same address
        for (var i = 0; i < entries.Count; i++)
        {
            // Stryker disable once Statement : an entry matched by ID was moved to its first address, where the address pass would find the same place
            if (matched[i] is not null)
                continue;
            foreach (var address in entries[i].Places)
            {
                if (cache.PlaceAt(address) is { } place)
                {
                    matched[i] = place;
                    break;
                }
            }
        }

        MoveByWebsite(cache, entries, matched, moved);

        for (var i = 0; i < entries.Count; i++)
        {
            // A place merged into another by a later move has no owner; the survivor carries its marks
            if (matched[i] is { Owner: null } merged)
                matched[i] = cache.PlaceAt(merged.Address);
            if (matched[i] is { } place)
                Learn(place, entries[i], retitle: moved.Contains(place));
        }

        return matched;
    }

    /// <summary>Moves the place, noting it so its title and shown address follow the entry it moved to.</summary>
    private static PlaceMark Moved(ListingCache cache, PlaceMark place, AddressKey to, HashSet<PlaceMark> moved)
    {
        if (place.Address == to)
            return place;
        var now = cache.Move(place, to);
        moved.Add(now);
        return now;
    }

    /// <summary>
    /// 3. A website the place has seen, shown by an unmatched entry at another address, moves the
    /// place there when every entry still at its own address shows other websites, as a new occupant
    /// would. An entry there showing that website, or none at all (an ad without links may be the
    /// venue's own), keeps the place where it is.
    /// </summary>
    private static void MoveByWebsite(ListingCache cache, IReadOnlyList<MarkEntry> entries, PlaceMark?[] matched, HashSet<PlaceMark> moved)
    {
        var byWebsite = new Dictionary<string, PlaceMark>(StringComparer.Ordinal);
        foreach (var place in cache.Places)
        {
            foreach (var site in place.Websites)
                byWebsite.TryAdd(site, place);
        }

        // Stryker disable once Statement : with no websites kept nothing can move; this only skips the work
        if (byWebsite.Count == 0)
            return;

        // Websites shown at each address this time, and the addresses an entry without one lists, to
        // tell whether a place's venue may still be there
        var shownAt = new Dictionary<AddressKey, HashSet<string>>();
        var unlinkedAt = new HashSet<AddressKey>();
        for (var i = 0; i < entries.Count; i++)
        {
            var websites = Websites(entries[i]).ToList();
            foreach (var address in entries[i].Places)
            {
                if (websites.Count == 0)
                    unlinkedAt.Add(address);
                if (!shownAt.TryGetValue(address, out var sites))
                    shownAt[address] = sites = new HashSet<string>(StringComparer.Ordinal);
                sites.UnionWith(websites);
            }
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (matched[i] is not null || entries[i].Places.Count == 0)
                continue;
            foreach (var site in Websites(entries[i]))
            {
                if (!byWebsite.TryGetValue(site, out var place) || place.Owner is null || place.Address == entries[i].Places[0]
                    || unlinkedAt.Contains(place.Address) || (shownAt.TryGetValue(place.Address, out var there) && there.Contains(site)))
                    continue;

                // The entries at the old address matched it by address; they are another venue's now
                for (var j = 0; j < matched.Length; j++)
                {
                    if (ReferenceEquals(matched[j], place))
                        matched[j] = null;
                }

                matched[i] = Moved(cache, place, entries[i].Places[0], moved);
                break;
            }
        }
    }

    /// <summary>Keeps the entry's FFXIV Venues IDs, websites and Discord invites on the place, and its title and address as shown.</summary>
    /// <param name="retitle">
    /// Take the entry's title and address as shown even if the place has them: when it is marked or
    /// moves. Otherwise they are only filled in, so two entries at one place don't take turns.
    /// </param>
    internal static void Learn(PlaceMark place, MarkEntry entry, bool retitle)
    {
        var changed = false;
        if ((retitle || place.Title.Length == 0) && entry.Places.Contains(place.Address))
        {
            changed = !string.Equals(place.Title, entry.Title, StringComparison.Ordinal) || !string.Equals(place.ShownAddress, entry.ShownAddress, StringComparison.Ordinal);
            place.Title = entry.Title;
            place.ShownAddress = entry.ShownAddress;
        }

        foreach (var venue in entry.Members.Where(m => m.Source == ListingSource.FfxivVenues))
            changed |= place.VenueIds.Add(venue.Id);
        foreach (var uri in entry.Links)
        {
            if (DiscordCode(uri) is { } code)
                changed |= place.Discords.Add(code);
            else if (WebsiteKey(uri) is { } site)
                changed |= place.Websites.Add(site);
        }

        if (changed)
            place.Changed();
    }

    /// <summary>
    /// Moves the favourite and hidden marks of the entry's listings, and visited from all but its
    /// event, to the place at its first address. Does nothing for an entry with no plot or apartment.
    /// </summary>
    private static void Adopt(ListingCache cache, MarkEntry entry)
    {
        if (entry.Places.Count == 0 || !entry.Members.Any(m => m.IsMarked))
            return;
        PlaceMark? place = cache.PlaceAt(entry.Places[0]);
        foreach (var member in entry.Members)
        {
            foreach (var mark in Enum.GetValues<EntryMark>())
            {
                if (!EntryMarks.Get(member, mark) || (mark == EntryMark.Visited && ReferenceEquals(member, entry.Event)))
                    continue;
                // Stryker disable once Assignment : Place returns the place already at the address, so a second call gives the same one
                place ??= cache.Place(entry.Places[0]);
                switch (mark)
                {
                    case EntryMark.Favourite:
                        place.Favourite = true;
                        break;
                    case EntryMark.Visited:
                        place.Visited = true;
                        break;
                    default:
                        place.Hidden = true;
                        break;
                }

                EntryMarks.Set(member, mark, false);
            }
        }
    }

    private static IEnumerable<string> Websites(MarkEntry entry) =>
        entry.Links.Where(u => DiscordCode(u) is null).Select(WebsiteKey).OfType<string>();

    /// <summary>A Discord invite's code (case kept, as codes are case-sensitive), or null for any other link.</summary>
    internal static string? DiscordCode(Uri uri)
    {
        if (!WebLinks.IsDiscordInvite(uri))
            return null;
        var path = uri.AbsolutePath.TrimEnd('/');
        return path[(path.LastIndexOf('/') + 1)..];
    }

    /// <summary>
    /// A website as places compare it: host without "www." and path without a trailing slash, lower
    /// case; no scheme, query or fragment. Null for a link that isn't a venue's (<see cref="NotVenueHosts"/>).
    /// </summary>
    internal static string? WebsiteKey(Uri uri)
    {
        if (!WebLinks.IsWeb(uri))
            return null;
        var host = (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host).ToLowerInvariant();
        if (NotVenueHosts.Contains(host) || NotVenueHosts.Any(h => host.EndsWith("." + h, StringComparison.Ordinal)))
            return null;
        return host + uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
    }
}
