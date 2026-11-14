namespace Jaunt.Core.Listings;

/// <summary>A listing kept between sessions, with the user's marks on it.</summary>
public sealed class CachedListing
{
    public required ListingSource Source { get; init; }

    /// <summary>The listing's ID within its source.</summary>
    public required string Id { get; init; }

    public string Title { get; set; } = "";

    /// <summary>When any source last listed it.</summary>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>
    /// A single event rather than a recurring venue or event. One-offs can't reliably be matched to a
    /// later listing, so they expire even when marked.
    /// </summary>
    public bool OneOff { get; set; }

    public bool Favourite { get; set; }
    public bool Visited { get; set; }
    public bool Hidden { get; set; }

    /// <summary>Favourite, visited and hidden listings are never cleared, and expire only if one-off.</summary>
    public bool IsMarked => Favourite || Visited || Hidden;
}
