using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jaunt.Core.Listings;

/// <summary>
/// Reads the Party Finder listings xivpf collects (<see cref="Url"/>) and records the ads in the None
/// category, where venues and events advertise, in the listing cache. The rules are in
/// <c>docs/design.md</c> under "xivpf".
/// </summary>
/// <remarks>
/// The response is every listing xivpf holds, about 700 listings and 1.6 MB, of which about 150 are in
/// the None category. Reading it is one pass over the listings; a listing that doesn't have the
/// expected shape is skipped rather than failing the rest.
/// </remarks>
public static class Xivpf
{
    public const string Url = "https://xivpf.com/api/listings";

    private const string NoneCategory = "None";

    /// <summary>One ad as the game would send it.</summary>
    /// <param name="Id">The Party Finder listing ID, the same one the game gives the listing.</param>
    /// <param name="Description">The ad's text, glyphs included.</param>
    /// <param name="ListedOn">The world the ad was put up on, or null if xivpf doesn't say.</param>
    public sealed record Ad(string Id, string Description, string? ListedOn);

    /// <summary>Reads the API's response, keeping only ads in the None category.</summary>
    /// <exception cref="JsonException">The text is not a JSON array.</exception>
    public static IReadOnlyList<Ad> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException("xivpf did not send a list of listings");

        var ads = new List<Ad>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (Read(element) is { } ad)
                ads.Add(ad);
        }

        return ads;
    }

    /// <summary>
    /// Records that xivpf listed these ads now. An ad the in-game Party Finder has also sent counts as
    /// seen now there too, so the in-game copy, which the list prefers, shows when it was last listed.
    /// </summary>
    public static void Record(ListingCache cache, IEnumerable<Ad> ads, DateTimeOffset now)
    {
        foreach (var ad in ads)
        {
            cache.SeenAd(ListingSource.Xivpf, ad.Id, ad.Description, ad.ListedOn, now);
            if (cache.Find(ListingSource.InGamePartyFinder, ad.Id) is { } inGame)
                cache.SeenElsewhere(inGame, now);
        }
    }

    /// <summary>
    /// Whether the list shows the in-game copy of this listing instead: it is from xivpf and the
    /// in-game Party Finder has sent the same listing.
    /// </summary>
    public static bool ShownInGame(ListingCache cache, CachedListing listing) =>
        listing.Source == ListingSource.Xivpf && cache.Find(ListingSource.InGamePartyFinder, listing.Id) is not null;

    private static Ad? Read(JsonElement element)
    {
        ItemData? data;
        try
        {
            data = element.Deserialize<ItemData>();
        }
        catch (JsonException)
        {
            return null;
        }

        var listing = data?.Listing;
        if (listing?.Id is not { } id || listing.Category != NoneCategory)
            return null;

        var description = Description(listing.Description);
        if (description is null)
            return null;

        var world = listing.CreatedWorld?.Name;
        return new Ad(
            id.ToString(CultureInfo.InvariantCulture),
            description,
            string.IsNullOrWhiteSpace(world) ? null : world.Trim());
    }

    /// <summary>
    /// The English text, as the address parser reads it best. xivpf gives the ad once per client
    /// language; they differ only in auto-translate phrases. Any other language when English is missing.
    /// </summary>
    private static string? Description(Dictionary<string, string?>? texts)
    {
        if (texts is null)
            return null;
        if (texts.TryGetValue("en", out var english) && !string.IsNullOrWhiteSpace(english))
            return english;
        return texts.Values.FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
    }

    // The API's shapes, only the fields Jaunt reads. Others, such as the recruiter's name and the
    // party's jobs, are ignored.
    private sealed class ItemData
    {
        [JsonPropertyName("listing")]
        public ListingData? Listing { get; set; }
    }

    private sealed class ListingData
    {
        [JsonPropertyName("id")]
        public ulong? Id { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("description")]
        public Dictionary<string, string?>? Description { get; set; }

        [JsonPropertyName("created_world")]
        public WorldData? CreatedWorld { get; set; }
    }

    private sealed class WorldData
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
