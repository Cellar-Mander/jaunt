namespace Jaunt.Core.Text;

/// <summary>
/// The place names the address parser recognises: worlds, data centres and districts from the game's
/// data, plus the abbreviations and Japanese names players use for them (<see cref="CommunityAliases"/>).
/// </summary>
public sealed class Gazetteer
{
    internal enum Keyword
    {
        None,
        WardSuffix, // 区
        WardPrefix, // 第
        PlotSuffix, // 番地, 番
        RoomSuffix, // 号室, 号
        Apartment, // アパ, apt
        Building, // an apartment building's name
        Room, // 個室, room
        Subdivision, // 拡張
        MainDivision, // 表
    }

    internal sealed class Entry
    {
        public District? District { get; set; }

        /// <summary>Worlds this name means; more than one only when regions share it.</summary>
        public List<GameWorld> Worlds { get; } = [];

        /// <summary>A world's full name, trusted anywhere in an ad rather than only beside an address.</summary>
        public bool FullName { get; set; }

        public bool DataCenter { get; set; }

        public Keyword Keyword { get; set; }
    }

    private const int MinPrefixLength = 3;

    // Common words that are also a world's prefix and that ads put next to addresses.
    private static readonly HashSet<string> NotPrefixes = ["gil"];

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameWorld> worldsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> cjkWords = new(StringComparer.Ordinal);

    /// <param name="worlds">Every public world.</param>
    /// <param name="districtNames">Each district's names, in any of the game's languages.</param>
    public Gazetteer(IEnumerable<GameWorld> worlds, IEnumerable<KeyValuePair<District, string>> districtNames)
    {
        foreach (var world in worlds)
        {
            worldsByName[world.Name] = world;
            AddWorld(world.Name, world, fullName: true);
            Add(world.DataCenter).DataCenter = true;
        }

        foreach (var (name, world) in CommunityAliases.Worlds)
        {
            if (worldsByName.TryGetValue(world, out var w))
                AddWorld(name, w, fullName: CommunityAliases.FullJapaneseNames.Contains(name));
        }

        foreach (var name in CommunityAliases.DataCenters)
            Add(name).DataCenter = true;
        foreach (var (district, name) in districtNames.Concat(CommunityAliases.Districts))
            Add(name).District = district;
        foreach (var (name, keyword) in CommunityAliases.Keywords)
            Add(name).Keyword = keyword;
    }

    internal IReadOnlySet<string> CjkWords => cjkWords;

    internal int MaxCjkWordLength { get; private set; } = 1;

    /// <summary>The most tokens any name spans.</summary>
    internal int MaxNameTokens { get; private set; } = 1;

    internal GameWorld? FindWorld(string? name) => name is not null && worldsByName.TryGetValue(name, out var w) ? w : null;

    internal Entry? Find(string key) => entries.GetValueOrDefault(key);

    /// <summary>
    /// The world whose name starts with <paramref name="word"/>, which ads use as an abbreviation
    /// ("Krak", "Sagi"): the one in <paramref name="region"/>, or when none there starts with it, the
    /// one anywhere. Null when there is none, or several where it looked.
    /// </summary>
    internal GameWorld? WorldByPrefix(string word, string? region)
    {
        if (word.Length < MinPrefixLength || NotPrefixes.Contains(word))
            return null;

        GameWorld? inRegion = null, anywhere = null;
        int countInRegion = 0, count = 0;
        foreach (var world in worldsByName.Values)
        {
            if (!world.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
                continue;
            anywhere = world;
            count++;
            if (world.Region == region)
            {
                inRegion = world;
                countInRegion++;
            }
        }

        return countInRegion switch
        {
            1 => inRegion,
            0 when count == 1 => anywhere,
            _ => null,
        };
    }

    /// <summary>The lookup key for a run of tokens: their texts joined by spaces.</summary>
    internal static string Key(IEnumerable<string> texts) => string.Join(' ', texts);

    private void AddWorld(string name, GameWorld world, bool fullName)
    {
        var entry = Add(name);
        if (!entry.Worlds.Contains(world))
            entry.Worlds.Add(world);
        entry.FullName |= fullName;
    }

    private Entry Add(string name)
    {
        // CJK runs are kept whole here; they become the words that split runs in ad text.
        var tokens = AddressTokenizer.Tokenize(PartyFinderText.Normalise(name).Text, null, 0).Where(t => t.Kind != TokenKind.End).ToList();
        foreach (var t in tokens.Where(t => t.Kind == TokenKind.Cjk))
        {
            cjkWords.Add(t.Text);
            MaxCjkWordLength = Math.Max(MaxCjkWordLength, t.Text.Length);
        }

        MaxNameTokens = Math.Max(MaxNameTokens, tokens.Count);
        var key = Key(tokens.Select(t => t.Text));
        if (!entries.TryGetValue(key, out var entry))
            entries[key] = entry = new Entry();
        return entry;
    }
}
