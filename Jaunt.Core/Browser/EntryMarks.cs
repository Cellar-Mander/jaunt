using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>A mark the browser's buttons set on an entry.</summary>
public enum EntryMark
{
    Favourite,
    Visited,
    Hidden,
}

/// <summary>What carries a mark set on an entry, for its button's tooltip.</summary>
public enum MarkTarget
{
    /// <summary>The entry's housing address (<see cref="PlaceMark"/>).</summary>
    Place,

    /// <summary>A Partake or FFXIV RP Event Calendar event, by its ID.</summary>
    Event,

    /// <summary>Each of the entry's listings, by ID: ads with no plot or apartment to mark.</summary>
    Listings,
}

/// <summary>One entry as the marks see it.</summary>
/// <param name="Members">Its listings, title first.</param>
/// <param name="Places">The plot or apartment addresses its listings give, the one it shows first; empty when none names more than the ward.</param>
/// <param name="Title">As shown, for the hidden list.</param>
/// <param name="ShownAddress">Its first address as shown and copied, for the hidden list.</param>
/// <param name="Links">Every member's links.</param>
public sealed record MarkEntry(IReadOnlyList<CachedListing> Members, IReadOnlyList<AddressKey> Places, string Title, string ShownAddress, IReadOnlyList<Uri> Links)
{
    /// <summary>Its Partake or RP calendar event, if it has one.</summary>
    public CachedListing? Event => Members.FirstOrDefault(m => m.Source is ListingSource.Partake or ListingSource.RpEventCalendar);
}

/// <summary>
/// The favourite, visited and hidden buttons. A mark goes on the entry's housing address, so it
/// carries over to every listing there; visited on a Partake or RP calendar event goes on that event,
/// so a venue's regular nights aren't marked by one event there. See "Marks" under "Listing browser"
/// in <c>docs/design.md</c>.
/// </summary>
public static class EntryMarks
{
    /// <summary>
    /// What a mark goes on: visited on the event if there is one; otherwise the address, if the entry
    /// names a plot or apartment; otherwise the event, else each listing.
    /// </summary>
    public static MarkTarget Target(MarkEntry entry, EntryMark mark)
    {
        if (mark == EntryMark.Visited && entry.Event is not null)
            return MarkTarget.Event;
        if (entry.Places.Count > 0)
            return MarkTarget.Place;
        return entry.Event is not null ? MarkTarget.Event : MarkTarget.Listings;
    }

    /// <summary>Whether the entry shows the mark: its place carries it, or any of its listings does.</summary>
    /// <param name="place">The place <see cref="PlaceMarks.Match"/> found for the entry, or null.</param>
    public static bool Has(MarkEntry entry, PlaceMark? place, EntryMark mark) =>
        (place is not null && Get(place, mark)) || entry.Members.Any(m => Get(m, mark));

    /// <summary>
    /// Sets the mark on its <see cref="Target"/> when the entry doesn't show it, keeping what is seen
    /// at a newly marked place; otherwise clears it from the place and every listing, whoever set it.
    /// </summary>
    /// <param name="place">The entry's place, or null; set to the place marked, if one is.</param>
    /// <returns>Whether the entry shows the mark afterwards.</returns>
    public static bool Toggle(ListingCache cache, MarkEntry entry, ref PlaceMark? place, EntryMark mark)
    {
        if (Has(entry, place, mark))
        {
            if (place is not null)
                Set(place, mark, false);
            foreach (var member in entry.Members)
                Set(member, mark, false);
            return false;
        }

        switch (Target(entry, mark))
        {
            case MarkTarget.Place:
                place ??= cache.Place(entry.Places[0]);
                Set(place, mark, true);
                PlaceMarks.Learn(place, entry, retitle: true);
                break;
            case MarkTarget.Event:
                Set(entry.Event!, mark, true);
                break;
            default:
                foreach (var member in entry.Members)
                    Set(member, mark, true);
                break;
        }

        return true;
    }

    internal static bool Get(CachedListing listing, EntryMark mark) => mark switch
    {
        EntryMark.Favourite => listing.Favourite,
        EntryMark.Visited => listing.Visited,
        _ => listing.Hidden,
    };

    private static bool Get(PlaceMark place, EntryMark mark) => mark switch
    {
        EntryMark.Favourite => place.Favourite,
        EntryMark.Visited => place.Visited,
        _ => place.Hidden,
    };

    internal static void Set(CachedListing listing, EntryMark mark, bool value)
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

    private static void Set(PlaceMark place, EntryMark mark, bool value)
    {
        switch (mark)
        {
            case EntryMark.Favourite:
                place.Favourite = value;
                break;
            case EntryMark.Visited:
                place.Visited = value;
                break;
            default:
                place.Hidden = value;
                break;
        }
    }
}
