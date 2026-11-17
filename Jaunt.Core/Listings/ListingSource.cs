namespace Jaunt.Core.Listings;

/// <summary>Where a listing came from. Saved by number, so only ever append.</summary>
public enum ListingSource
{
    /// <summary>Party Finder ads the game sends while the player has the Party Finder open.</summary>
    InGamePartyFinder,
    Xivpf,
    FfxivVenues,
    Partake,
    RpEventCalendar,

    /// <summary>Venue and event adverts players shout in chat (<see cref="Shouts"/>), kept like Party Finder ads.</summary>
    Shout,
}

public static class ListingSources
{
    /// <summary>
    /// Whether the source's listings are ads: free text, glyphs included, which the parsers read for
    /// title, addresses, times, links and performers (<see cref="CachedListing.Description"/>).
    /// </summary>
    public static bool IsAd(this ListingSource source) =>
        source is ListingSource.InGamePartyFinder or ListingSource.Xivpf or ListingSource.Shout;
}
