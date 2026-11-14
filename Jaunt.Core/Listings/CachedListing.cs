namespace Jaunt.Core.Listings;

/// <summary>A listing kept between sessions, with the user's marks on it.</summary>
public sealed class CachedListing
{
    public required ListingSource Source { get; init; }

    /// <summary>The listing's ID within its source.</summary>
    public required string Id { get; init; }

    public string Title { get; set; } = "";

    /// <summary>The ad's text as the source last sent it, glyphs included, for the parsers.</summary>
    public string Description { get; set; } = "";

    /// <summary>The world the source last listed it on, if it says; addresses that name no world use it.</summary>
    public string? ListedOn { get; set; }

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
