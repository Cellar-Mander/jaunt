using Jaunt.Core.Listings;

namespace Jaunt.Core.Browser;

/// <summary>A mark the browser's buttons set on an entry.</summary>
public enum EntryMark
{
    Favourite,
    Visited,
    Hidden,
}

/// <summary>
/// Which of an entry's listings carry its favourite, visited and hidden marks. A venue is an FFXIV
/// Venues listing; an event is a Partake or FFXIV RP Event Calendar one. See "Marks" under "Listing
/// browser" in <c>docs/design.md</c>.
/// </summary>
public static class EntryMarks
{
    /// <summary>
    /// The listings a mark is set on: for favourite and hidden the venue, else the event; for visited
    /// the event, else the venue. An entry with neither (only ads) marks every ad, so a venue's several
    /// ads all carry it.
    /// </summary>
    /// <param name="members">The entry's listings, title first.</param>
    public static IReadOnlyList<CachedListing> Targets(IReadOnlyList<CachedListing> members, EntryMark mark)
    {
        var venue = members.FirstOrDefault(m => m.Source == ListingSource.FfxivVenues);
        var item = members.FirstOrDefault(m => m.Source is ListingSource.Partake or ListingSource.RpEventCalendar);
        var target = mark == EntryMark.Visited ? item ?? venue : venue ?? item;
        return target is null ? members : [target];
    }

    /// <summary>Whether the entry shows the mark: any of its listings carries it, whoever set it.</summary>
    public static bool Has(IReadOnlyList<CachedListing> members, EntryMark mark) => members.Any(m => Get(m, mark));

    /// <summary>
    /// Sets the mark on its <see cref="Targets"/> when the entry doesn't show it; otherwise clears it
    /// from every listing, so a mark set before the entry merged with another still comes off.
    /// </summary>
    /// <returns>Whether the entry shows the mark afterwards.</returns>
    public static bool Toggle(IReadOnlyList<CachedListing> members, EntryMark mark)
    {
        if (Has(members, mark))
        {
            foreach (var member in members)
                Set(member, mark, false);
            return false;
        }

        foreach (var target in Targets(members, mark))
            Set(target, mark, true);
        return true;
    }

    private static bool Get(CachedListing listing, EntryMark mark) => mark switch
    {
        EntryMark.Favourite => listing.Favourite,
        EntryMark.Visited => listing.Visited,
        _ => listing.Hidden,
    };

    private static void Set(CachedListing listing, EntryMark mark, bool value)
    {
        switch (mark)
        {
            case EntryMark.Favourite:
                listing.Favourite = value;
                break;
            case EntryMark.Visited:
                listing.Visited = value;
                break;
            default:
                listing.Hidden = value;
                break;
        }
    }
}
