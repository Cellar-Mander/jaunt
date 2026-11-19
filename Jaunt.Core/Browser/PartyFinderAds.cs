using System.Globalization;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>Whether the game's Party Finder can open an ad, or why not.</summary>
public enum PartyFinderState
{
    /// <summary>Still listed, on the data centre the character is on or one this can't tell.</summary>
    Openable,

    /// <summary>Not seen for longer than an ad stays listed (<see cref="Schedules.StillListed"/>).</summary>
    NotListed,

    /// <summary>Listed on another data centre, whose ads the game's Party Finder doesn't show.</summary>
    OtherDataCentre,
}

/// <summary>
/// The Party Finder ad an entry's Party Finder button opens: its listing ID, which the game and xivpf
/// share, whether it can be opened, when it was last seen and the data centre it is listed on, if known.
/// </summary>
public readonly record struct PartyFinderAd(ulong ListingId, PartyFinderState State, DateTimeOffset LastSeen, string? DataCentre);

public static class PartyFinderAds
{
    /// <summary>
    /// The in-game or xivpf Party Finder ad among an entry's listings to open: the one listed longest
    /// (<see cref="Schedules.ListedUntil"/>), the first on a tie. Null when none is such an ad with a
    /// numeric ID.
    /// </summary>
    /// <param name="currentDataCentre">The data centre the character is on, or null when unknown, which counts as any.</param>
    public static PartyFinderAd? Choose(
        IEnumerable<CachedListing> listings, DateTimeOffset now, int cooldownMinutes, Gazetteer gazetteer, string? currentDataCentre)
    {
        CachedListing? best = null;
        ulong bestId = 0;
        var bestUntil = DateTimeOffset.MinValue;
        foreach (var listing in listings)
        {
            var id = IdOf(listing);
            if (id is null)
                continue;
            var until = Schedules.ListedUntil(listing.Source, listing.LastSeen, cooldownMinutes);
            if (best is null || until > bestUntil)
            {
                best = listing;
                bestId = id.Value;
                bestUntil = until;
            }
        }

        if (best is null)
            return null;

        var dataCentre = gazetteer.FindWorld(best.ListedOn?.Trim())?.DataCenter;
        var state = now >= bestUntil ? PartyFinderState.NotListed
            : dataCentre is not null && currentDataCentre is not null && !string.Equals(dataCentre, currentDataCentre.Trim(), StringComparison.OrdinalIgnoreCase)
                ? PartyFinderState.OtherDataCentre
                : PartyFinderState.Openable;
        return new PartyFinderAd(bestId, state, best.LastSeen, dataCentre);
    }

    /// <summary>A Party Finder ad's listing ID; null for other sources, or an ID that isn't a number.</summary>
    private static ulong? IdOf(CachedListing listing)
    {
        if (listing.Source is not (ListingSource.InGamePartyFinder or ListingSource.Xivpf))
            return null;
        return ulong.TryParse(listing.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
