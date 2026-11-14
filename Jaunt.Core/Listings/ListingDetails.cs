using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>What the parsers read from one ad: its addresses and website links, in the order written.</summary>
public sealed record ListingDetails(IReadOnlyList<HousingAddress> Addresses, IReadOnlyList<WebLink> Links)
{
    /// <summary>The longest title taken from an ad, in characters, before it is cut with an ellipsis.</summary>
    public const int MaxTitleLength = 60;

    /// <summary>Parses an ad's text, as the game sends it.</summary>
    /// <param name="listedOn">The world the ad is listed on, for addresses that name none.</param>
    public static ListingDetails Parse(AddressParser parser, string adText, string? listedOn) =>
        new(parser.Parse(adText, listedOn), WebLinks.Find(adText));

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
        return line.Length <= MaxTitleLength ? line : line[..(MaxTitleLength - 1)].TrimEnd() + "…";
    }
}
