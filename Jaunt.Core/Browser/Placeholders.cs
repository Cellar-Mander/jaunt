using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>
/// What the browser shows for an entry without an icon or splash image of its own. See "Placeholders" under
/// "Icons and splash images" in <c>docs/design.md</c>.
/// </summary>
public static class Placeholders
{
    // What the place is comes before what happens there
    private static readonly Interests[] IconOrder =
    [
        Interests.Clubs, Interests.Bars, Interests.Cafes, Interests.Spa, Interests.Gambling, Interests.LiveMusic,
        Interests.Games, Interests.Art, Interests.Photography, Interests.Tarot, Interests.Roleplay, Interests.Chill,
    ];

    /// <summary>The one interest whose pictogram stands in for the entry's icon, or <see cref="Interests.None"/> if it has none.</summary>
    public static Interests IconInterest(Interests interests)
    {
        foreach (var interest in IconOrder)
        {
            if ((interests & interest) != 0)
                return interest;
        }

        return Interests.None;
    }
}

/// <summary>
/// The game art that stands in for an entry's splash image: its housing district's, else the zone its
/// written location starts with, else the world map. See "Placeholders" under "Icons and splash images" in <c>docs/design.md</c>.
/// </summary>
public sealed class Backdrops
{
    private readonly IReadOnlyDictionary<District, string> districts;
    private readonly (Word[] Words, string Art)[] zones;
    private readonly string? anywhere;

    /// <param name="districts">Each housing district's art.</param>
    /// <param name="zones">Each town and field zone's name, as the game writes it in English, and its art.</param>
    /// <param name="anywhere">The art for an entry that names no place with art of its own: the world map.</param>
    public Backdrops(IReadOnlyDictionary<District, string> districts, IEnumerable<(string Name, string Art)> zones, string? anywhere)
    {
        this.districts = districts;
        this.anywhere = anywhere;
        this.zones = [.. zones.Select(z => (Words(z.Name), z.Art)).Where(z => z.Item1.Length > 0)];
    }

    /// <summary>
    /// The art for the district of the first address that names one with art; else for the zone the first
    /// location that names one starts with ("Central Shroud, The Bannock"); else the world map's, if any.
    /// </summary>
    public string? For(IReadOnlyList<HousingAddress> addresses, IEnumerable<string?> locations)
    {
        foreach (var address in addresses)
        {
            if (address.District is { } district && districts.TryGetValue(district, out var art))
                return art;
        }

        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location))
                continue;
            if (Zone(Words(location)) is { } art)
                return art;
        }

        return anywhere;
    }

    // The zone whose name the location's words start with, the longest if several do
    private string? Zone(Word[] location)
    {
        string? found = null;
        var longest = 0;
        foreach (var (name, art) in zones)
        {
            if (name.Length > longest && StartsWith(location, name))
            {
                found = art;
                longest = name.Length;
            }
        }

        return found;
    }

    // A written word matches the name's word in any case; one ending in a full stop ("Cen.") matches any word it begins
    private static bool StartsWith(Word[] location, Word[] name)
    {
        if (location.Length < name.Length)
            return false;
        for (var i = 0; i < name.Length; i++)
        {
            var written = location[i];
            var matches = written.Abbreviated
                ? name[i].Text.StartsWith(written.Text, StringComparison.OrdinalIgnoreCase)
                : string.Equals(written.Text, name[i].Text, StringComparison.OrdinalIgnoreCase);
            if (!matches)
                return false;
        }

        return true;
    }

    private readonly record struct Word(string Text, bool Abbreviated);

    // Runs of letters, digits and apostrophes ("Ul'dah"); anything else separates them
    private static Word[] Words(string text)
    {
        var words = new List<Word>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inWord = i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '\'' or '’');
            if (inWord && start < 0)
            {
                start = i;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(new Word(text[start..i].Replace('’', '\''), i < text.Length && text[i] == '.'));
                start = -1;
            }
        }

        return [.. words];
    }
}
