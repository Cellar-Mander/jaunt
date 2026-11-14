using System.Text.Json.Serialization;

namespace Jaunt.Core.Listings;

/// <summary>A listing kept between sessions, with the user's marks on it.</summary>
public sealed class CachedListing
{
    public required ListingSource Source { get; init; }

    /// <summary>The listing's ID within its source.</summary>
    public required string Id { get; init; }

    /// <summary>The title, or for FFXIV Venues the venue's name as sent, which <see cref="ShownTitle"/> reads.</summary>
    public string Title { get; set; } = "";

    /// <summary>The title as the windows show and sort it, derived from <see cref="Title"/> for sources that send raw names.</summary>
    [JsonIgnore]
    public string ShownTitle => Source == ListingSource.FfxivVenues ? FfxivVenues.Title(Title) : Title;

    /// <summary>The ad's text as the source last sent it, glyphs included, for the parsers.</summary>
    public string Description { get; set; } = "";

    /// <summary>The world the source last listed it on, if it says; addresses that name no world use it.</summary>
    public string? ListedOn { get; set; }

    /// <summary>
    /// A venue's fields as FFXIV Venues last sent them, which <see cref="FfxivVenues.Show"/> reads;
    /// null for other sources.
    /// </summary>
    public FfxivVenues.Fields? Venue { get; set; }

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
