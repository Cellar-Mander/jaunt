namespace Jaunt.Core.Listings;

/// <summary>
/// A listing kept between sessions, with the user's marks on it. Only the fields as the source sent
/// them, when it was last seen and the marks are saved; the rest is derived when read.
/// </summary>
public sealed class CachedListing
{
    private string title = "";
    private string? titledFrom;
    private string adTitle = "";
    private bool favourite;
    private bool visited;
    private bool hidden;

    public required ListingSource Source { get; init; }

    /// <summary>The listing's ID within its source.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The title, or for FFXIV Venues, Partake and the FFXIV RP Event Calendar the name or title as
    /// sent, which <see cref="ShownTitle"/> reads. A Party Finder ad with text is titled from it
    /// (<see cref="ListingDetails.Title"/>), worked out on first read after the text changes, so
    /// loading the cache doesn't title every ad; what is set is used only while it has no text.
    /// </summary>
    public string Title
    {
        get
        {
            if (Source is not (ListingSource.InGamePartyFinder or ListingSource.Xivpf) || Description.Length == 0)
                return title;
            if (!ReferenceEquals(titledFrom, Description))
            {
                adTitle = ListingDetails.Title(Description);
                titledFrom = Description;
            }

            return adTitle;
        }
        set => title = value;
    }

    /// <summary>The title as the windows show and sort it, derived from <see cref="Title"/> for sources that send raw names.</summary>
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

    public bool Favourite
    {
        get => favourite;
        set => SetMark(ref favourite, value);
    }

    public bool Visited
    {
        get => visited;
        set => SetMark(ref visited, value);
    }

    public bool Hidden
    {
        get => hidden;
        set => SetMark(ref hidden, value);
    }

    /// <summary>Favourite, visited and hidden listings are never cleared, and expire only if one-off.</summary>
    public bool IsMarked => Favourite || Visited || Hidden;

    /// <summary>The cache holding it, told when a mark changes so the marks are saved; null while it is in none.</summary>
    internal ListingCache? Owner { get; set; }

    /// <summary>
    /// Whether a source has listed it (or the cache file it was read from had it); false for one known
    /// only from the saved marks, which no source cache file holds.
    /// </summary>
    internal bool Recorded { get; set; }

    private void SetMark(ref bool mark, bool value)
    {
        if (mark == value)
            return;
        mark = value;
        Owner?.MarksChanged();
    }
}
