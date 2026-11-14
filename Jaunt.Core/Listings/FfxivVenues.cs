using System.Globalization;
using System.Text.Json;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Reads the venue list from FFXIV Venues (<see cref="Url"/>) and records it in the listing cache.
/// The rules are in <c>docs/design.md</c> under "FFXIV Venues".
/// </summary>
/// <remarks>
/// The response is every venue at once, about 2.5 MB for 1,400 venues. Reading it is one pass over
/// the venues; a venue that doesn't have the expected shape is skipped rather than failing the rest.
/// </remarks>
public static class FfxivVenues
{
    public const string Url = "https://api.ffxivvenues.com/v1.0/venue";

    /// <summary>A misspelt world ("Bismark") must share at least this many letters with the real one.</summary>
    private const int MinWorldPrefix = 5;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, District> Districts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mist"] = District.Mist,
        ["Lavender Beds"] = District.LavenderBeds,
        ["The Lavender Beds"] = District.LavenderBeds,
        ["Goblet"] = District.Goblet,
        ["The Goblet"] = District.Goblet,
        ["Shirogane"] = District.Shirogane,
        ["Empyreum"] = District.Empyreum,
    };

    /// <summary>One venue as Jaunt keeps it.</summary>
    /// <param name="Address">Null when the venue gives no housing address (a free-text location, or none).</param>
    public sealed record Venue(string Id, string Name, HousingAddress? Address);

    /// <summary>Reads the API's response.</summary>
    /// <param name="gazetteer">Worlds, to correct a misspelt world name.</param>
    /// <exception cref="JsonException">The text is not a JSON array.</exception>
    public static IReadOnlyList<Venue> Parse(string json, Gazetteer gazetteer)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new JsonException("FFXIV Venues did not send a list of venues");

        var venues = new List<Venue>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (Read(element, gazetteer) is { } venue)
                venues.Add(venue);
        }

        return venues;
    }

    /// <summary>Records that FFXIV Venues listed these venues now.</summary>
    public static void Record(ListingCache cache, IEnumerable<Venue> venues, DateTimeOffset now)
    {
        foreach (var venue in venues)
        {
            var listing = cache.Seen(ListingSource.FfxivVenues, venue.Id, venue.Name, now);
            listing.Address = venue.Address;
        }
    }

    private static Venue? Read(JsonElement element, Gazetteer gazetteer)
    {
        VenueData? data;
        try
        {
            data = element.Deserialize<VenueData>(JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (data is null || string.IsNullOrWhiteSpace(data.Id) || data.Approved == false)
            return null;

        return new Venue(data.Id, Title(data.Name), Address(data.Location, gazetteer));
    }

    private static string Title(string? name)
    {
        var text = (name ?? "").Trim();
        var lineEnd = text.IndexOfAny(['\r', '\n']);
        // Stryker disable once Equality : the name is trimmed, so it never starts with a line break and lineEnd is never 0
        return ListingDetails.Shorten(lineEnd < 0 ? text : text[..lineEnd].TrimEnd());
    }

    /// <summary>
    /// The venue's housing address: a known district and a ward of 1 to 30, with a plot of 1 to 60 or
    /// else an apartment. Null otherwise, such as for a venue that gives only a free-text location.
    /// </summary>
    internal static HousingAddress? Address(LocationData? location, Gazetteer gazetteer)
    {
        if (location?.World is not { } written || string.IsNullOrWhiteSpace(written)
            || location.District is null || !Districts.TryGetValue(location.District.Trim(), out var district)
            || location.Ward is < 1 or > 30)
            return null;

        int? plot = location.Plot is >= 1 and <= 60 ? location.Plot : null;
        int? apartment = plot is null && location.Apartment > 0 ? location.Apartment : null;
        var room = location.Room > 0 ? location.Room.ToString(CultureInfo.InvariantCulture) : null;
        return new HousingAddress(World(written.Trim(), gazetteer), WorldSource.Text, district, location.Ward, plot, location.Subdivision, apartment, room);
    }

    /// <summary>
    /// The world's name as the game writes it. A misspelling is corrected to the one world sharing
    /// its longest beginning of at least <see cref="MinWorldPrefix"/> letters; an unknown name is kept as written.
    /// </summary>
    internal static string World(string written, Gazetteer gazetteer)
    {
        if (gazetteer.FindWorld(written) is { } world)
            return world.Name;

        for (var length = written.Length - 1; length >= MinWorldPrefix; length--)
        {
            if (gazetteer.WorldByPrefix(written[..length], region: null) is { } match)
                return match.Name;
        }

        return written;
    }

    // The API's shapes, only the fields Jaunt reads. Others, such as managers' Discord IDs, are ignored.
    internal sealed class VenueData
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public LocationData? Location { get; set; }
        public bool? Approved { get; set; }
    }

    internal sealed class LocationData
    {
        public string? World { get; set; }
        public string? District { get; set; }
        public int Ward { get; set; }
        public int Plot { get; set; }
        public int Apartment { get; set; }
        public int Room { get; set; }
        public bool Subdivision { get; set; }
    }
}
