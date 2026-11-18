using System.Globalization;
using System.Text;

namespace Jaunt.Core.Text;

/// <summary>A point on a zone's map, in the coordinates the game shows ("X: 9.2 Y: 10.8").</summary>
public readonly record struct MapPoint(float X, float Y);

/// <summary>A place a zone's map labels ("The Bannock"), and where.</summary>
public sealed record ZoneSpot(string Name, MapPoint Point);

/// <summary>A town or field zone, from the game's data.</summary>
/// <param name="Territory">Its <c>TerritoryType</c> row.</param>
/// <param name="Map">Its map's row.</param>
/// <param name="Name">Its English name, as the game writes it.</param>
/// <param name="Teleport">The English name of the zone its aetheryte is in, which is its own unless it has none (Old Gridania's is New Gridania's).</param>
/// <param name="Aetheryte">Where its aetheryte is on its map; null when the aetheryte is in another zone.</param>
/// <param name="Spots">The places its map labels.</param>
public sealed record GameZone(uint Territory, uint Map, string Name, string Teleport, MapPoint? Aetheryte, IReadOnlyList<ZoneSpot> Spots);

/// <summary>An in-game place outside housing that a listing names.</summary>
/// <param name="Zone">The zone it is in.</param>
/// <param name="World">The world it is on; null when unknown.</param>
/// <param name="ListedWorld">Whether the world is the one the ad is listed on, as the text names none.</param>
/// <param name="Spot">The spot in the zone it names: a map label's name or "Aetheryte"; null for the zone alone.</param>
/// <param name="Point">Where on the zone's map: the coordinates written, else the spot's; null when neither is known.</param>
public sealed record GamePlace(GameZone Zone, string? World, bool ListedWorld, string? Spot, MapPoint? Point)
{
    /// <summary>"World · Zone · Spot (9.2, 10.8)", leaving out what isn't known.</summary>
    public string Display()
    {
        var text = new StringBuilder();
        if (World is { Length: > 0 } world)
            text.Append(world).Append(" · ");
        text.Append(Zone.Name);
        if (Spot is not null)
            text.Append(" · ").Append(Spot);
        if (Point is { } p)
            text.Append(CultureInfo.InvariantCulture, $" ({p.X:0.0}, {p.Y:0.0})");
        return text.ToString();
    }

    /// <summary>
    /// What Copy copies: "World, tp Zone", which Lifestream's <c>/li</c> command reads as a visit to the
    /// world and a teleport to the zone's aetheryte (or the aetheryte serving it); "tp Zone" without a world.
    /// </summary>
    public string Copied() => World is { Length: > 0 } world ? $"{world}, tp {Zone.Teleport}" : $"tp {Zone.Teleport}";
}

/// <summary>
/// Finds the in-game places outside housing that a listing's location or ad text names: a town or field
/// zone, then optionally a spot its map labels, its aetheryte, or map coordinates. See "Places" under
/// "Listing browser" in <c>docs/design.md</c>.
/// </summary>
public sealed class GamePlaces
{
    /// <summary>The spot <see cref="GamePlace.Spot"/> names for a zone's aetheryte.</summary>
    public const string AetheryteSpot = "Aetheryte";

    // What players call a city that is no zone's name, and the zone they mean
    private static readonly (string Name, string Zone)[] Aliases =
    [
        ("Limsa", "Limsa Lominsa Lower Decks"), ("Limsa Lominsa", "Limsa Lominsa Lower Decks"),
        ("Gridania", "New Gridania"), ("Ul'dah", "Ul'dah - Steps of Nald"), ("Uldah", "Ul'dah - Steps of Nald"),
        ("Ishgard", "Foundation"),
    ];

    // Highest map coordinate written for a place; the largest maps run to about 42
    private const float MaxCoordinate = 43;

    private readonly (string[] Name, GameZone Zone)[] zoneNames;
    private readonly Dictionary<GameZone, (string[] Name, ZoneSpot Spot)[]> spotNames = [];

    /// <param name="zones">Every town and field zone outside housing.</param>
    public GamePlaces(IEnumerable<GameZone> zones)
    {
        var byName = new Dictionary<string, GameZone>(StringComparer.OrdinalIgnoreCase);
        var names = new List<(string[], GameZone)>();
        foreach (var zone in zones)
        {
            byName.TryAdd(zone.Name, zone);
            names.AddRange(Variants(zone.Name).Select(n => (n, zone)));
            spotNames[zone] = [.. zone.Spots.SelectMany(s => Variants(s.Name).Select(n => (n, s)))];
        }

        foreach (var (alias, name) in Aliases)
        {
            if (byName.TryGetValue(name, out var zone))
                names.Add((Texts(alias), zone));
        }

        zoneNames = [.. names];
    }

    /// <summary>
    /// The places an event's or venue's location names ("Central Shroud, The Bannock", "Kugane, See URL"):
    /// a zone alone counts, as the field holds only a location.
    /// </summary>
    /// <param name="world">The event's world, or null when unknown.</param>
    public IReadOnlyList<GamePlace> InLocation(string? location, string? world) =>
        string.IsNullOrWhiteSpace(location) ? [] : Find(location, world, listedWorld: false, needPoint: false);

    /// <summary>
    /// The places an ad's text names ("Limsa X 9.2 Y 10.8", "Sophia, Limsa Aetheryte"): only a zone
    /// followed by a spot, its aetheryte or coordinates counts, as ads name cities for their theme
    /// ("Ishgard's finest pub") or a player's progress ("Mor Dhona is my highest point").
    /// </summary>
    /// <param name="text">The ad's normalised text (<see cref="PartyFinderText.Normalise"/>).</param>
    /// <param name="listedOn">The world the ad is listed on, as the text's world; null when unknown.</param>
    public IReadOnlyList<GamePlace> InAd(string text, string? listedOn) => Find(text, listedOn, listedWorld: true, needPoint: true);

    private List<GamePlace> Find(string text, string? world, bool listedWorld, bool needPoint)
    {
        var words = Words(text);
        var found = new List<GamePlace>();
        var i = 0;
        while (i < words.Count)
        {
            if (Longest(zoneNames, words, i) is not ({ } zone, var length))
            {
                i++;
                continue;
            }

            i += length;
            string? spot = null;
            MapPoint? point = null;
            var end = words[i - 1].End;
            var written = Coordinates(text, end);
            if (written is null)
            {
                if (Follows(text, words, i) && Longest(spotNames[zone], words, i) is ({ } s, var spotLength))
                {
                    spot = s.Name.Replace('\n', ' ');
                    point = s.Point;
                    i += spotLength;
                    end = words[i - 1].End;
                }
                else if (Follows(text, words, i) && zone.Aetheryte is { } aetheryte && Is(words[i], "aetheryte"))
                {
                    spot = AetheryteSpot;
                    point = aetheryte;
                    i++;
                    if (i < words.Count && Is(words[i], "plaza"))
                        i++;
                    end = words[i - 1].End;
                }

                written = Coordinates(text, end);
            }

            if (written is var (at, after))
            {
                point = at;
                while (i < words.Count && words[i].Start < after)
                    i++;
            }

            if (needPoint && point is null)
                continue;
            var place = new GamePlace(zone, world, listedWorld && world is not null, spot, point);
            if (!found.Contains(place))
                found.Add(place);
        }

        return found;
    }

    // A zone's or spot's name, and without its leading "The" ("The Pillars" written "Pillars")
    private static IEnumerable<string[]> Variants(string name)
    {
        // A label over two lines ("Everschade\n(The Guardian Tree)") is matched by its first
        var texts = Texts(name.Split('\n')[0]);
        yield return texts;
        if (texts is [var first, _, ..] && string.Equals(first, "the", StringComparison.OrdinalIgnoreCase))
            yield return texts[1..];
    }

    private static string[] Texts(string name) => [.. Words(name).Select(w => w.Text)];

    // The longest name that the words at the index spell, and how many words it takes
    private static (T? Value, int Length) Longest<T>((string[] Name, T Value)[] names, List<Word> words, int index)
        where T : class
    {
        T? best = null;
        var longest = 0;
        foreach (var (name, value) in names)
        {
            // Stryker disable once Equality : the game's names in a list are distinct, so no two of one length spell the same words
            if (name.Length > longest && Spells(words, index, name))
            {
                best = value;
                longest = name.Length;
            }
        }

        return (best, longest);
    }

    // A written word matches the name's word in any case; one ending in a full stop ("Cen.") matches any word it begins
    private static bool Spells(List<Word> words, int index, string[] name)
    {
        if (words.Count - index < name.Length)
            return false;
        for (var k = 0; k < name.Length; k++)
        {
            var written = words[index + k];
            var matches = written.Abbreviated
                ? name[k].StartsWith(written.Text, StringComparison.OrdinalIgnoreCase)
                : string.Equals(written.Text, name[k], StringComparison.OrdinalIgnoreCase);
            if (!matches)
                return false;
        }

        return true;
    }

    // Whether the word at the index comes straight after the one before, past separators only
    private static bool Follows(string text, List<Word> words, int index)
    {
        if (index >= words.Count)
            return false;
        for (var k = words[index - 1].End; k < words[index].Start; k++)
        {
            if (!IsSeparator(text[k]))
                return false;
        }

        return true;
    }

    private static bool IsSeparator(char c) => c is ' ' or '\t' or ',' or ':' or '-' or '–' or '|' or '@' or '/' or '(';

    private static bool Is(Word word, string text) => string.Equals(word.Text, text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Map coordinates written at the position, past separators and an "at": "X 9.2 Y 10.8", "(21, 25)",
    /// "10.0, 12.0". Two bare numbers need a comma between them, a time ("9, 10pm") isn't coordinates, and each must be on a map.
    /// </summary>
    private static (MapPoint Point, int End)? Coordinates(string text, int start)
    {
        var i = start;
        while (i < text.Length && text[i] != '(' && IsSeparator(text[i]))
            i++;
        if (i + 2 < text.Length && string.Compare(text, i, "at", 0, 2, StringComparison.OrdinalIgnoreCase) == 0 && char.IsWhiteSpace(text[i + 2]))
            i = SkipSpace(text, i + 2);
        var paren = i < text.Length && text[i] == '(';
        if (paren)
            i = SkipSpace(text, i + 1);
        var labelledX = Label(text, ref i, 'x');
        if (Number(text, ref i) is not { } x)
            return null;
        var afterX = i;
        i = SkipSpace(text, i);
        var comma = i < text.Length && text[i] == ',';
        if (comma)
            i = SkipSpace(text, i + 1);
        if (i == afterX)
            return null;
        var labelledY = Label(text, ref i, 'y');
        if (Number(text, ref i) is not { } y || IsTime(text, i))
            return null;
        if (!(labelledX && labelledY) && !comma && !paren)
            return null;
        if (x is < 1 or >= MaxCoordinate || y is < 1 or >= MaxCoordinate)
            return null;
        return (new MapPoint(x, y), i);
    }

    private static int SkipSpace(string text, int i)
    {
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        return i;
    }

    // "X", "x:", "X =" before a number, consumed when there
    private static bool Label(string text, ref int i, char axis)
    {
        if (i >= text.Length || char.ToLowerInvariant(text[i]) != axis)
            return false;
        var j = SkipSpace(text, i + 1);
        if (j < text.Length && text[j] is ':' or '=')
            j = SkipSpace(text, j + 1);
        // Stryker disable once Boolean : i still points at the letter, so the number after it is never read and the label never counts
        if (j >= text.Length || !char.IsAsciiDigit(text[j]))
            return false;
        i = j;
        return true;
    }

    // One or two digits with any decimals ("9", "10.8"); null for a longer number ("830") or a clock time ("9:30")
    private static float? Number(string text, ref int i)
    {
        var start = i;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
            i++;
        if (i == start || i - start > 2)
            return null;
        if (i + 1 < text.Length && text[i] == '.' && char.IsAsciiDigit(text[i + 1]))
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
                i++;
        }
        else if (i < text.Length && text[i] == ':')
        {
            return null;
        }

        return float.Parse(text.AsSpan(start, i - start), CultureInfo.InvariantCulture);
    }

    // "pm", " a.m." after a number
    private static bool IsTime(string text, int i)
    {
        i = SkipSpace(text, i);
        if (i >= text.Length || char.ToLowerInvariant(text[i]) is not ('a' or 'p'))
            return false;
        i++;
        if (i < text.Length && text[i] == '.')
            i++;
        return i < text.Length && char.ToLowerInvariant(text[i]) == 'm' && (i + 1 == text.Length || !char.IsLetter(text[i + 1]));
    }

    private readonly record struct Word(string Text, bool Abbreviated, int Start, int End);

    /// <summary>
    /// Runs of letters, digits and apostrophes ("Ul'dah"), split where letters meet digits and where a
    /// lower-case letter meets an upper-case one ("LimsaLominsa"); anything else separates them.
    /// </summary>
    private static List<Word> Words(string text)
    {
        var words = new List<Word>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : ' ';
            var inWord = char.IsLetterOrDigit(c) || c is '\'' or '’';
            var split = start >= 0 && inWord && ((char.IsLower(text[i - 1]) && char.IsUpper(c)) || char.IsDigit(text[i - 1]) != char.IsDigit(c));
            if (start >= 0 && (!inWord || split))
            {
                words.Add(new Word(text[start..i].Replace('’', '\''), c == '.', start, i));
                start = -1;
            }

            if (inWord && start < 0)
                start = i;
        }

        return words;
    }
}
