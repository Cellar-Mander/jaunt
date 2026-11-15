using Jaunt.Core.Browser;

namespace Jaunt.Core.Listings;

/// <summary>
/// Favourite, visited and hidden marks on a venue's housing address rather than on one listing, so
/// they carry over to every listing there from any source, including Party Finder ads posted after
/// the first one expired. Keeps what was seen there while marked (FFXIV Venues IDs, websites and
/// Discord invites), which <see cref="PlaceMarks"/> uses to follow the venue when it moves.
/// </summary>
public sealed class PlaceMark
{
    private bool favourite;
    private bool visited;
    private bool hidden;

    internal PlaceMark(AddressKey address) => Address = address;

    /// <summary>Where the venue is, as far as the marks know; changes when it moves.</summary>
    public AddressKey Address { get; internal set; }

    /// <summary>The title last shown for a listing here, for the hidden list.</summary>
    public string Title { get; internal set; } = "";

    /// <summary>The address as last shown and copied ("World, District, W20, P28"), for the hidden list.</summary>
    public string ShownAddress { get; internal set; } = "";

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

    public bool IsMarked => Favourite || Visited || Hidden;

    /// <summary>FFXIV Venues IDs seen here while marked.</summary>
    internal HashSet<string> VenueIds { get; } = new(StringComparer.Ordinal);

    /// <summary>Websites seen here while marked, by <see cref="PlaceMarks.WebsiteKey"/>.</summary>
    internal HashSet<string> Websites { get; } = new(StringComparer.Ordinal);

    /// <summary>Discord invite codes seen here while marked.</summary>
    internal HashSet<string> Discords { get; } = new(StringComparer.Ordinal);

    /// <summary>The cache holding it, told when it changes so the marks are saved; null once it is dropped.</summary>
    internal ListingCache? Owner { get; set; }

    internal void Changed() => Owner?.MarksChanged();

    private void SetMark(ref bool mark, bool value)
    {
        if (mark == value)
            return;
        mark = value;
        Changed();
    }
}
