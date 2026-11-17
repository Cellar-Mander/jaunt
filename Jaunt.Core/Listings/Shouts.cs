using System.Security.Cryptography;
using System.Text;
using Jaunt.Core.Browser;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Venue and event adverts shouted in chat. A shout is kept only when it reads as an advert: it gives
/// a housing address with a district and ward (the world, when it names none, is the one it was heard
/// on). Each shouter has one listing, holding their latest advert, so a repeated shout updates it rather
/// than adding another. The shouter's name is never stored: the ID is a hash of it.
/// </summary>
public static class Shouts
{
    /// <summary>
    /// The listing ID for a shouter: the first 16 hex digits of the SHA-256 of their name and home
    /// world, so it stays the same between sessions without keeping the name.
    /// </summary>
    /// <param name="homeWorld">The shouter's home world, or null when the game doesn't say.</param>
    public static string IdOf(string sender, string? homeWorld)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sender.Trim()}@{homeWorld?.Trim()}"));
        return Convert.ToHexStringLower(bytes, 0, 8);
    }

    /// <summary>Whether the shout reads as a venue or event advert: it gives an address with a district and ward.</summary>
    /// <param name="heardOn">The world the shout was heard on, for an address that names none.</param>
    public static bool IsAdvert(AddressParser parser, string text, string? heardOn) =>
        parser.Parse(text, heardOn).Any(a => AddressKey.Of(a) is not null);

    /// <summary>
    /// Records the shout as its shouter's listing when it is an advert (<see cref="IsAdvert"/>), the
    /// same way as a Party Finder ad (<see cref="ListingCache.SeenAd"/>). A shout that isn't an advert
    /// changes nothing, so the shouter's last advert stays.
    /// </summary>
    /// <param name="id">The shouter's ID (<see cref="IdOf"/>).</param>
    /// <param name="text">The shout as the game sends it, glyphs included.</param>
    /// <param name="heardOn">The world the shout was heard on; the listing is listed on it.</param>
    /// <returns>The listing, or null when the shout isn't an advert.</returns>
    public static CachedListing? Record(ListingCache cache, AddressParser parser, string id, string text, string? heardOn, DateTimeOffset now) =>
        IsAdvert(parser, text, heardOn) ? cache.SeenAd(ListingSource.Shout, id, text, heardOn, now) : null;
}
