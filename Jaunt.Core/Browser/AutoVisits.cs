using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>
/// Marks an entry visited when the player is at its plot or apartment while it is open (Settings →
/// Listings, on by default). An address naming only the ward, or a location in the open world, never
/// counts. Each entry is marked at most once per stay, so one the player marks not visited while still
/// there stays that way; a stay ends when the player leaves the address. See "Marks" under "Listing
/// browser" in <c>docs/design.md</c>.
/// </summary>
public sealed class AutoVisits
{
    private AddressKey? here;
    private readonly HashSet<CachedListing> handled = new(ReferenceEqualityComparer.Instance);

    /// <summary>Where the player is now, as last told.</summary>
    public AddressKey? Here => here;

    /// <summary>
    /// Tells it where the player is: a plot (on it or inside the house) or an apartment, or null
    /// anywhere else. A different address starts a new stay.
    /// </summary>
    /// <returns>Whether the player is at a plot or apartment, so entries are worth checking.</returns>
    public bool At(AddressKey? address)
    {
        if (address is { WardOnly: true })
            address = null;
        if (address != here)
        {
            here = address;
            handled.Clear();
        }

        return here is not null;
    }

    /// <summary>
    /// Marks the entry visited, as its button would, if it is at the player's address, one of its
    /// <paramref name="listed"/> openings is in progress, and it hasn't been handled this stay. An entry
    /// already showing the mark counts as handled.
    /// </summary>
    /// <param name="place">The entry's place, or null; set to the place marked, if one is.</param>
    /// <returns>Whether it marked the entry.</returns>
    public bool Check(ListingCache cache, MarkEntry entry, ref PlaceMark? place, IReadOnlyList<Occurrence> listed, DateTimeOffset now)
    {
        if (here is not { } address || !entry.Places.Contains(address) || !IsOpen(listed, now))
            return false;

        var seen = false;
        foreach (var member in entry.Members)
            seen |= !handled.Add(member);
        if (seen || EntryMarks.Has(entry, place, EntryMark.Visited))
            return false;

        return EntryMarks.Toggle(cache, entry, ref place, EntryMark.Visited);
    }

    private static bool IsOpen(IReadOnlyList<Occurrence> listed, DateTimeOffset now)
    {
        foreach (var occurrence in listed)
        {
            if (occurrence.IsOpen(now))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The address of the plot the player stands on, from the game's 0-based ward and plot indexes;
    /// null off a plot (a negative plot index) or when the world or district is unknown.
    /// </summary>
    public static AddressKey? OnPlot(string? world, District? district, int wardIndex, int plotIndex) =>
        plotIndex < 0 || wardIndex < 0 ? null : AddressKey.Of(new HousingAddress(world, WorldSource.Text, district, wardIndex + 1, plotIndex + 1));

    /// <summary>
    /// The address of the house or apartment the player is inside, from the game's indoor house ID:
    /// 0-based ward and plot indexes, the apartment division (0 main, 1 subdivision) and the room
    /// number, which for an apartment is its number and is 0 in the building's lobby. Null in a lobby,
    /// a workshop, or when the world or district is unknown.
    /// </summary>
    public static AddressKey? Inside(string? world, District? district, int wardIndex, bool apartment, int division, int plotIndex, int room, bool workshop)
    {
        if (workshop || wardIndex < 0)
            return null;
        if (apartment)
        {
            return room <= 0
                ? null
                : AddressKey.Of(new HousingAddress(world, WorldSource.Text, district, wardIndex + 1, null, division == 1, room));
        }

        return plotIndex < 0 ? null : AddressKey.Of(new HousingAddress(world, WorldSource.Text, district, wardIndex + 1, plotIndex + 1));
    }
}
