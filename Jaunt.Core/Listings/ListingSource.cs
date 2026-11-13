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
}
