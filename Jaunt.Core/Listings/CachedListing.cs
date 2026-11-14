using System.Text.Json.Serialization;

namespace Jaunt.Core.Listings;

/// <summary>A listing kept between sessions, with the user's marks on it.</summary>
public sealed class CachedListing
{
    public required ListingSource Source { get; init; }

    /// <summary>The listing's ID within its source.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The title, or for FFXIV Venues, Partake and the FFXIV RP Event Calendar the name or title as
    /// sent, which <see cref="ShownTitle"/> reads.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>The title as the windows show and sort it, derived from <see cref="Title"/> for sources that send raw names.</summary>
    [JsonIgnore]
    public string ShownTitle => Source switch
    {
        ListingSource.FfxivVenues => FfxivVenues.Title(Title),
        ListingSource.Partake => Partake.Title(Title),
        ListingSource.RpEventCalendar => RpEventCalendar.Title(Title),
        _ => Title,
    };

    /// <summary>The ad's text as the source last sent it, glyphs included, for the parsers.</summary>
    public string Description { get; set; } = "";

    /// <summary>The world the source last listed it on, if it says; addresses that name no world use it.</summary>
    public string? ListedOn { get; set; }

    /// <summary>
    /// A venue's fields as FFXIV Venues last sent them, which <see cref="FfxivVenues.Show"/> reads;
    /// null for other sources.
    /// </summary>
    public FfxivVenues.Fields? Venue { get; set; }

    /// <summary>
    /// The links as the source wrote them, for sources whose links are checked only when shown
    /// (Partake: <see cref="Partake.Show"/> keeps http and https ones). Empty for other sources.
    /// </summary>
    public IReadOnlyList<string> LinkTexts { get; set; } = [];

    /// <summary>The data centre as the source gives it, for sources that say (Partake).</summary>
    public string? DataCentre { get; set; }

    /// <summary>
    /// The free-text location as the source gives it (Partake, the FFXIV RP Event Calendar), for the
    /// address parser. Null for sources whose addresses are structured or in <see cref="Description"/>.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>When a dated event starts, for sources that list them (Partake).</summary>
    public DateTimeOffset? StartsAt { get; set; }

    /// <summary>When a dated event ends; always after <see cref="StartsAt"/> when both are set.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>The times the source last said it is open, earliest first, for sources that list occurrences.</summary>
    public IReadOnlyList<ListingOpening> Openings { get; set; } = [];

    /// <summary>
    /// The day the source's moderators last checked it, for sources that validate listings (the FFXIV
    /// RP Event Calendar); null if never or for other sources.
    /// </summary>
    public DateOnly? LastValidated { get; set; }

    /// <summary>
    /// The content rating the source gives, as sent ("Teen", "Mature"); null if none. Kept for
    /// filtering; not shown. For the RP calendar, <see cref="RpEventCalendar.Show"/> falls back to the
    /// name's rating tag.
    /// </summary>
    public string? Rating { get; set; }

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
