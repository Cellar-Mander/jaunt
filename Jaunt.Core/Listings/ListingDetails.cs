using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>What the parsers read from one ad: its addresses and website links, in the order written.</summary>
public sealed record ListingDetails(IReadOnlyList<HousingAddress> Addresses, IReadOnlyList<WebLink> Links)
{
    /// <summary>The performers the ad names (<see cref="Text.Performers.Find"/>); empty for listings that aren't ads.</summary>
    public IReadOnlyList<Performer> Performers { get; init; } = [];

    /// <summary>The longest title taken from an ad, in characters, before it is cut with an ellipsis.</summary>
    public const int MaxTitleLength = 60;

    /// <summary>Parses an ad's text, as the game sends it.</summary>
    /// <param name="listedOn">The world the ad is listed on, for addresses that name none.</param>
    public static ListingDetails Parse(AddressParser parser, string adText, string? listedOn)
    {
        var links = WebLinks.Find(adText);
        return new(parser.Parse(adText, listedOn), links) { Performers = Text.Performers.Find(adText, links) };
    }

    /// <summary>
    /// A title for an ad, which has none of its own: the first line of its normalised text, cut to
    /// <see cref="MaxTitleLength"/> characters with "…" at the end when longer. Empty for an empty ad.
    /// </summary>
    public static string Title(string adText)
    {
        var text = PartyFinderText.Normalise(adText).Text;
        var lineEnd = text.IndexOf('\n', StringComparison.Ordinal);
        // Stryker disable once Equality : normalised text is trimmed, so it never starts with a line break and lineEnd is never 0
        var line = lineEnd < 0 ? text : text[..lineEnd];
        return Shorten(line);
    }

    /// <summary>A title cut to <see cref="MaxTitleLength"/> characters, with "…" at the end when longer.</summary>
    internal static string Shorten(string title) =>
        title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)].TrimEnd() + "…";
}
