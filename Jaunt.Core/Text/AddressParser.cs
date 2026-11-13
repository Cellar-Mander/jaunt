namespace Jaunt.Core.Text;

/// <summary>
/// Reads housing addresses from Party Finder ad text. The rules, and the shapes of address they cover,
/// are in <c>docs/design.md</c> under "Addresses in ad text".
/// </summary>
/// <remarks>
/// Finds each ward and plot first, since numbers are the part ads write most consistently, then reads
/// the district and world from the words just before it. Each step looks at a bounded number of
/// tokens, so the cost is linear in the ad's length.
/// </remarks>
public sealed class AddressParser(Gazetteer gazetteer)
{
    private const int MaxWard = 30;
    private const int MaxPlot = 60;

    /// <summary>Words skipped between an address and its district or world, and between those two.</summary>
    private const int DistrictReach = 6;
    private const int WorldReach = 3;

    private static readonly HashSet<string> TimeWords =
    [
        "am", "pm", "a", "p", "st", "et", "lt", "pt", "ct", "mt", "est", "edt", "pst", "pdt", "cst", "cdt", "mst",
        "mdt", "gmt", "bst", "cet", "cest", "aest", "aedt", "時",
    ];

    private static readonly HashSet<string> Ordinals = ["st", "nd", "rd", "th"];

    /// <summary>Reads every address in one ad.</summary>
    /// <param name="adText">The ad's text as the game sends it, glyphs included.</param>
    /// <param name="listedOn">The world the ad is listed on: the fallback when the text names none.</param>
    public IReadOnlyList<HousingAddress> Parse(string adText, string? listedOn)
    {
        var text = PartyFinderText.Normalise(adText).Text;
        var tokens = AddressTokenizer.Tokenize(text, gazetteer.CjkWords, gazetteer.MaxCjkWordLength);
        return new Run(gazetteer, tokens, listedOn).Parse();
    }

    private readonly record struct Match(int Length, District? District, GameWorld? World, bool DataCenter, bool FullName, Gazetteer.Keyword Keyword);

    private sealed class Core
    {
        public int Start { get; set; }
        public int End { get; set; }
        public District? District { get; set; }
        public GameWorld? World { get; set; }
        public int Ward { get; set; }
        public int? Plot { get; set; }
        public int? SecondPlot { get; set; }
        public bool WardExplicit { get; set; }
    }

    private sealed class Run(Gazetteer g, List<Token> t, string? listedOn)
    {
        private readonly GameWorld? listing = g.FindWorld(listedOn);
        private readonly List<HousingAddress> found = [];

        public List<HousingAddress> Parse()
        {
            var lower = 0;
            for (var i = 0; i < t.Count;)
            {
                if (TryCore(i) is { } core)
                {
                    Resolve(core, lower);
                    lower = i = core.End;
                }
                else
                {
                    i++;
                }
            }

            return found;
        }

        // ---- Finding the ward and plot ----

        private Core? TryCore(int i)
        {
            var m = MatchAt(i);
            if (m.District is { } d && DistrictLed(i, m.Length, d) is { } byDistrict)
                return byDistrict;
            if (m.World is { } w && m.Length > 0 && WorldLed(i, m.Length, w) is { } byWorld)
                return byWorld;
            return WardFirst(i) ?? PlotFirst(i);
        }

        /// <summary>"Mist 8-32", "ゴブ第23区32番地", "Gob28 P5".</summary>
        private Core? DistrictLed(int i, int length, District district)
        {
            var core = new Core { Start = i, District = district };
            var k = i + length;
            if (Is(k, Gazetteer.Keyword.WardPrefix))
            {
                k++;
                core.WardExplicit = true;
            }

            if (Number(k) is not (int ward and >= 1 and <= MaxWard))
                return null;
            core.Ward = ward;
            k++;
            if (Is(k, Gazetteer.Keyword.WardSuffix))
            {
                k++;
                core.WardExplicit = true;
            }

            if (PlotMarker(k) is { } marked)
            {
                core.Plot = marked;
                k += 2;
            }
            else if (Number(k) is int plot and >= 1 and <= MaxPlot && !Is(k + 1, Gazetteer.Keyword.RoomSuffix) && !LooksLikeTime(k + 1)
                && (core.WardExplicit || IsDashOrSpace(t[k].Gap)))
            {
                core.Plot = plot;
                k++;
                if (Is(k, Gazetteer.Keyword.PlotSuffix))
                    k++;
            }

            core.End = k;
            // A bare number after a district is not a ward on its own.
            return core.Plot is null && !core.WardExplicit ? null : core;
        }

        /// <summary>"Ifrit/6-51": a world, then a ward and plot with a dash, and no district.</summary>
        private Core? WorldLed(int i, int length, GameWorld world)
        {
            var k = i + length;
            if (Number(k) is not (int ward and >= 1 and <= MaxWard) || Number(k + 1) is not (int plot and >= 1 and <= MaxPlot)
                || !IsDash(t[k + 1].Gap) || Is(k + 2, Gazetteer.Keyword.RoomSuffix) || LooksLikeTime(k + 2))
                return null;
            var core = new Core { Start = i, World = world, Ward = ward, Plot = plot };
            core.End = k + 2;
            return core;
        }

        /// <summary>"W20 P28", "Ward 27 Plot 44", "W2/30", "P29/30", "Ward 2".</summary>
        private Core? WardFirst(int i)
        {
            if (WardSpec(i) is not ({ } ward, var k))
                return null;
            var core = new Core { Start = i, Ward = ward, WardExplicit = true };
            if (PlotMarker(k) is { } plot)
            {
                core.Plot = plot;
                k += 2;
                if (Number(k) is int second and >= 1 and <= MaxPlot && t[k].Gap.Trim() == "/")
                {
                    core.SecondPlot = second;
                    k++;
                }
            }
            else if (Number(k) is int bare and >= 1 and <= MaxPlot && t[k].Gap.Trim() is "/" or "|" && !LooksLikeTime(k + 1))
            {
                core.Plot = bare;
                k++;
            }

            core.End = k;
            return core;
        }

        /// <summary>"Plot 40, Ward 5", "P50 W1", "Plot 30, 5th Ward".</summary>
        private Core? PlotFirst(int i)
        {
            if (PlotMarker(i) is not { } plot || WardSpec(i + 2) is not ({ } ward, var k))
                return null;
            var core = new Core { Start = i, Ward = ward, Plot = plot, WardExplicit = true };
            core.End = k;
            return core;
        }

        /// <summary>"W5", "Ward 5", "5th Ward": the ward and the index after it.</summary>
        private (int? Ward, int Next) WardSpec(int j)
        {
            if (Word(j) is "w" or "ward" && Number(j + 1) is int ward and >= 1 and <= MaxWard && IsMarkerGap(t[j + 1].Gap))
                return (ward, j + 2);
            if (Number(j) is int ordinal and >= 1 and <= MaxWard && Word(j + 1) is { } suffix && Ordinals.Contains(suffix)
                && t[j + 1].Gap.Length == 0 && Word(j + 2) == "ward")
                return (ordinal, j + 3);
            return (null, j);
        }

        /// <summary>"P5", "Plot 5": the plot, two tokens long.</summary>
        private int? PlotMarker(int j) =>
            Word(j) is "p" or "plot" && Number(j + 1) is int plot and >= 1 and <= MaxPlot && IsMarkerGap(t[j + 1].Gap) ? plot : null;

        // ---- Finding the district and world ----

        private void Resolve(Core core, int lower)
        {
            var district = core.District;
            var world = core.World;
            if (world is null)
                ScanBack(core.Start - 1, lower, ref district, ref world);
            if (district is null && core.Plot is null)
                return; // "Ward 2" alone is not an address

            var address = world is null
                ? new HousingAddress(listing?.Name ?? listedOn, WorldSource.Listing, district, core.Ward, core.Plot)
                : new HousingAddress(world.Name, WorldSource.Text, district, core.Ward, core.Plot);
            found.Add(address);
            if (core.SecondPlot is { } second)
                found.Add(address with { Plot = second }); // "P29/30": two adjoining plots
        }

        /// <summary>
        /// Walks back from an address: the nearest district, then the nearest world before it. A token
        /// that could be either ("Gob": Goblet or Goblin) is the district when none has been found yet.
        /// A data centre where the world would be means the ad names none.
        /// </summary>
        private void ScanBack(int j, int lower, ref District? district, ref GameWorld? world)
        {
            var reach = district is null ? DistrictReach : WorldReach;
            var skipped = 0;
            while (j >= lower)
            {
                var m = MatchEndingAt(j, lower);
                if (district is null && m.District is { } d)
                {
                    district = d;
                    j -= m.Length;
                    reach = WorldReach;
                    skipped = 0;
                    continue;
                }

                if (m.World is { } w)
                {
                    world = w;
                    return;
                }

                if (m.DataCenter)
                    return;
                j -= Math.Max(m.Length, 1);
                if (++skipped > reach)
                    return;
            }
        }

        // ---- Token matching ----

        /// <summary>The longest name starting at <paramref name="s"/>.</summary>
        private Match MatchAt(int s)
        {
            for (var length = Math.Min(g.MaxNameTokens, t.Count - s); length > 0; length--)
            {
                if (Lookup(s, length) is { Length: > 0 } m)
                    return m;
            }

            return default;
        }

        /// <summary>The longest name ending at <paramref name="e"/> and starting no earlier than <paramref name="lower"/>.</summary>
        private Match MatchEndingAt(int e, int lower)
        {
            for (var length = Math.Min(g.MaxNameTokens, e - lower + 1); length > 0; length--)
            {
                if (Lookup(e - length + 1, length) is { Length: > 0 } m)
                    return m;
            }

            return default;
        }

        private Match Lookup(int s, int length)
        {
            if (s < 0 || s + length > t.Count)
                return default;
            for (var j = s + 1; j < s + length; j++)
            {
                if (!IsNameGap(t[j].Gap))
                    return default;
            }

            var entry = g.Find(Gazetteer.Key(t.Skip(s).Take(length).Select(x => x.Text)));
            var world = entry?.Worlds.FirstOrDefault(w => w.Region == listing?.Region) ?? entry?.Worlds.FirstOrDefault();
            if (world is null && length == 1 && t[s].Kind == TokenKind.Word)
                world = g.WorldByPrefix(t[s].Text, listing?.Region);
            if (entry is null && world is null)
                return default;
            return new Match(length, entry?.District, world, entry?.DataCenter ?? false, entry?.FullName ?? false, entry?.Keyword ?? Gazetteer.Keyword.None);
        }

        private bool Is(int k, Gazetteer.Keyword keyword) => k < t.Count && MatchAt(k).Keyword == keyword;

        private int? Number(int k) => k >= 0 && k < t.Count && t[k].Kind == TokenKind.Number ? t[k].Value : null;

        private string? Word(int k) => k >= 0 && k < t.Count && t[k].Kind == TokenKind.Word ? t[k].Text : null;

        /// <summary>Whether the token at <paramref name="k"/> makes the numbers before it a time ("8-11pm", "19-5 18時").</summary>
        private bool LooksLikeTime(int k)
        {
            if (k >= t.Count)
                return false;
            if (t[k].Kind == TokenKind.Number)
                return t[k].Gap.StartsWith(':');
            return TimeWords.Contains(t[k].Text) && string.IsNullOrWhiteSpace(t[k].Gap);
        }

        private static bool IsMarkerGap(string gap) => gap.All(c => char.IsWhiteSpace(c) || c is ':' or '.' or '#');

        private static bool IsNameGap(string gap) => gap.All(c => char.IsWhiteSpace(c) || c is '.' or '・' or '·');

        private static bool IsDash(string gap) => gap.Trim() is "-" or "‐" or "‑" or "–" or "—" or "~";

        private static bool IsDashOrSpace(string gap) => IsDash(gap) || (gap.Length > 0 && string.IsNullOrWhiteSpace(gap));
    }
}
