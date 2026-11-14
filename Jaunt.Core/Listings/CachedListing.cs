using Jaunt.Core.Text;

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

    /// <summary>
    /// The address the source gives as data, for sources with structured addresses (FFXIV Venues).
    /// Null for ads, whose addresses are parsed from <see cref="Description"/>.
    /// </summary>
    public HousingAddress? Address { get; set; }

    /// <summary>The website and Discord links the source gives as data, http or https only.</summary>
    public IReadOnlyList<Uri> Links { get; set; } = [];

    /// <summary>The data centre the source gives, for sources that say (Partake).</summary>
    public string? DataCentre { get; set; }

    /// <summary>
    /// The free-text location the source gives as data (Partake), for the address parser. Null for
    /// sources whose addresses are structured or in <see cref="Description"/>.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>When a dated event starts, for sources that list them (Partake).</summary>
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>When a dated event ends; always after <see cref="StartsAt"/> when both are set.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>Open around the clock, as inferred from the source's schedule.</summary>
    public bool AlwaysOpen { get; set; }

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
