using System.Globalization;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

public static class PartyFinderAds
{
    /// <summary>
    /// The listing ID, which the game and xivpf share, of the in-game or xivpf Party Finder ad among an
    /// entry's listings that the game's Party Finder can open: the one listed longest
    /// (<see cref="Schedules.ListedUntil"/>, the first on a tie), if it is still listed and not on a world of
    /// another data centre than <paramref name="currentDataCentre"/>, whose ads the game doesn't show.
    /// Null otherwise, or when none is such an ad with a numeric ID. An unknown world counts as any data centre.
    /// </summary>
    /// <param name="currentDataCentre">The data centre the character is on, or null when unknown, which counts as any.</param>
    public static ulong? Openable(
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

        if (best is null || now >= bestUntil)
            return null;

        var dataCentre = gazetteer.FindWorld(best.ListedOn?.Trim())?.DataCenter;
        var elsewhere = dataCentre is not null && currentDataCentre is not null
            && !string.Equals(dataCentre, currentDataCentre.Trim(), StringComparison.OrdinalIgnoreCase);
        return elsewhere ? null : bestId;
    }

    /// <summary>A Party Finder ad's listing ID; null for other sources, or an ID that isn't a number.</summary>
    private static ulong? IdOf(CachedListing listing)
    {
        if (listing.Source is not (ListingSource.InGamePartyFinder or ListingSource.Xivpf))
            return null;
        return ulong.TryParse(listing.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    }
}
