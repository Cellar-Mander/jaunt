using System.Text.Json;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class PerformersTests
{
    /// <summary>The performers as "name@handle", "name" or "@handle", in order.</summary>
    private static string[] Find(string ad) =>
        [.. Performers.Find(ad, WebLinks.Find(ad)).Select(p => $"{p.Name}{(p.Twitch is null ? "" : "@" + Performers.HandleOf(p.Twitch.Uri))}")];

    [Theory]
    [InlineData("Tonight DJ Damsca, Gamba, Tarot, 50/50!", "Damsca")] // a singular DJ's comma list must go on with names
    [InlineData("Join Dj Seikat,Luckroll, Stella from 9pm", "Seikat", "Luckroll", "Stella")]
    [InlineData("Don't miss DJ Seikat, DJ Luckroll and DJ Stella from 9pm", "Seikat", "Luckroll", "Stella")]
    [InlineData("DJs: DJ_Mithval & DJ_Bunguin ♪ @Light", "DJ_Mithval", "DJ_Bunguin")]
    [InlineData("w/ DJ's EPYTEA & NYTEVYPR - Dyna-Mar-LB-W30,P3", "EPYTEA", "NYTEVYPR")] // & list ends at " - "
    [InlineData("DJ:WeedCat-Memo-SwayWave-Yuyu-Syren Mad Mist W6 P5", "WeedCat", "Memo", "SwayWave", "Yuyu", "Syren")]
    [InlineData("DJ SwayWave/Keshi/Cynaxia Dyn-Raff-Mist-W11-P45", "SwayWave", "Keshi", "Cynaxia")] // a different separator ends it
    [InlineData("DJ Eiro x Vesca x Yams x Lilith | Gamba", "Eiro", "Vesca", "Yams", "Lilith")]
    [InlineData("LIVE DJs: Erebus • Velo • Quade • Zapper | Raffle", "Erebus", "Velo", "Quade", "Zapper")] // a colon overrides LIVE
    [InlineData("DJ Treu - Stank Fang - Frost Queen Maya || 11PM-2AM EST", "Treu", "Stank Fang", "Frost Queen Maya")]
    [InlineData("DJ REIKO BEING GOOBY - REMNANT LOUNGE [SIREN] GOBLET", "REIKO")] // dashes that weren't a list
    [InlineData("DJ Damsa, Nola Elan, Kiki Nightshade and Sage Raves!", "Damsa", "Nola Elan", "Kiki Nightshade", "Sage Raves")]
    [InlineData("DJs KK, James Gracen 11pm-2am EST", "KK", "James Gracen")] // a number ends a name
    [InlineData("Music by Crying in Colour & Dejeanerates!", "Crying in Colour", "Dejeanerates")]
    [InlineData("MOTUKI | Bard Night | Naola Notes & Lyricons | 8pm", "Naola Notes", "Lyricons")]
    [InlineData("CLUB MOON presenting DJay Nightshade & Tinklage! Nov 5th", "DJay Nightshade", "Tinklage")]
    [InlineData("|DJKhaosVoid & Restless Corpse", "DJKhaosVoid", "Restless Corpse")]
    [InlineData("DJs-JamesGracen-Mystel-KioBeats Marilith Gob W15 P13", "JamesGracen", "Mystel", "KioBeats")]
    [InlineData("FT DJ 8bit! w/Courts", "8bit")]
    [InlineData("OPEN NOW | DJ Eiro LIVE NOW | Blackjack", "Eiro")]
    [InlineData("WE ARE OPEN DJ ILLIAX DJ INVIS! times", "ILLIAX", "INVIS")]
    [InlineData("great tunes by dj Roseheart and Dj Kittynip 10pm", "Roseheart", "Kittynip")]
    [InlineData("djayYAMS @ CLUB LUNA | Primal", "djayYAMS")]
    [InlineData("djKO @ Club", "djKO")] // a byline handle of two capitals
    [InlineData("DJ Afro Quincy at Extra Life night", "Afro Quincy")] // "at" ends a name
    [InlineData("DJ Solus Noctem, Bar Gamba", "Solus Noctem")] // so does a comma
    [InlineData("DJ Kio Bell 'Night Vibes'", "Kio Bell")] // and a quote
    [InlineData("DJ 2Bk!", "2Bk")] // digits then two letters
    [InlineData("DJ Kio, Bell | Gamba", "Kio")] // a singular DJ's comma list needs a third
    [InlineData("DJ Kio, Bell | 8 |", "Kio")]
    [InlineData("DJ Kio, Bell", "Kio")]
    [InlineData("DJ Kio, Bell and Rans", "Kio", "Bell", "Rans")] // or an "and"
    [InlineData("DJ's Kio, Bell | Gamba", "Kio", "Bell")] // plural markers don't
    [InlineData("DJs Kio, Bell | Gamba", "Kio", "Bell")]
    [InlineData("DJs: Kio, Bell | Gamba", "Kio", "Bell")]
    [InlineData("Music by Kio, Bell | Gamba", "Kio", "Bell")]
    [InlineData("presenting Kio, Bell | Gamba", "Kio", "Bell")]
    [InlineData("Bard Night | Kio, Bell | 8pm", "Kio", "Bell")]
    [InlineData("DJKhaosVoid, Corpse | Gamba", "DJKhaosVoid")] // a glued DJ is singular
    [InlineData("DJs: DJ Kio & DJ Bell", "Kio", "Bell")] // a repeated DJ starts a new list
    [InlineData("DJ Kio, DJ Bell | Gamba", "Kio", "Bell")]
    [InlineData("DJs Kio & Bell & Rans!", "Kio", "Bell", "Rans")] // an & list goes on with &
    [InlineData("DJs Kio, Bell & Rans & Sky!", "Kio", "Bell", "Rans")] // after another kind, & gives the last
    [InlineData("DJs Kio, Bell & Rans, Sky!", "Kio", "Bell", "Rans")]
    [InlineData("DJs Kio, Bell and Gamba!", "Kio", "Bell")]
    [InlineData("DJs Kio, Bell and Rans Sky Mist W6", "Kio", "Bell", "Rans")] // a last name needs an ending too
    [InlineData("DJs KK, Moon Cat, and Sleepy!", "KK", "Moon Cat", "Sleepy")]
    [InlineData("DJ Treu - Stank - Maya tonite", "Treu", "Stank", "Maya")]
    [InlineData("DJ Kio Bell - Rans - Sky Lounge [Siren]", "Kio")] // dashes that weren't a list
    [InlineData("DJs Kio Bell - | Gamba", "Kio")] // a dash with no name after isn't a separator
    [InlineData("DJs Kio Bell Rans Sky!", "Kio")] // at most three words
    [InlineData("Music by Daughters of Hydaelyn & Kio!", "Daughters of Hydaelyn", "Kio")]
    [InlineData("Music by Tom the Bard & Kio!", "Tom the Bard", "Kio")]
    [InlineData("Music by Jean de Arc & Kio!", "Jean de Arc", "Kio")]
    [InlineData("Music by Kio of", "Kio")]
    [InlineData("DJs Kio & 99", "Kio")]
    [InlineData("DJs Kio- Bell!", "Kio")] // only " - " and "-" with no spaces separate
    [InlineData("DJs Kio -Bell!", "Kio")]
    [InlineData("DJs Kio xBell", "Kio")] // " x " needs both spaces
    [InlineData("DJs Kio andrew", "Kio")] // and "and" a word of its own
    [InlineData("DJs Kio -", "Kio")]
    [InlineData("DJs Kio x", "Kio")]
    [InlineData("DJs Kio-", "Kio")]
    [InlineData("DJs Kio and", "Kio")]
    public void ReadsNamesAfterAMarker(string ad, params string[] expected) => Assert.Equal(expected, Find(ad));

    [Theory]
    [InlineData("live")]
    [InlineData("now")]
    [InlineData("and")]
    [InlineData("an")]
    [InlineData("more")]
    [InlineData("from")]
    [InlineData("tonight")]
    [InlineData("today")]
    [InlineData("open")]
    [InlineData("gamba")]
    [InlineData("tarot")]
    [InlineData("raffle")]
    [InlineData("steam")]
    [InlineData("giveaway")]
    [InlineData("giveaways")]
    [InlineData("auctions")]
    [InlineData("bar")]
    [InlineData("courts")]
    [InlineData("bingo")]
    [InlineData("drt")]
    [InlineData("dancers")]
    [InlineData("rp")]
    [InlineData("vibe")]
    [InlineData("at")]
    [InlineData("the")]
    [InlineData("on")]
    [InlineData("with")]
    [InlineData("w")]
    [InlineData("bj")]
    [InlineData("blackjack")]
    [InlineData("slots")]
    [InlineData("games")]
    [InlineData("prizes")]
    [InlineData("dance")]
    [InlineData("chat")]
    [InlineData("night")]
    [InlineData("afterparty")]
    [InlineData("in")]
    [InlineData("of")]
    [InlineData("for")]
    [InlineData("x")]
    [InlineData("dj")]
    [InlineData("djs")]
    [InlineData("dj's")]
    [InlineData("10mil")] // amounts, times and dates
    [InlineData("11pm")]
    [InlineData("10am")]
    [InlineData("1st")]
    [InlineData("2nd")]
    [InlineData("3rd")]
    [InlineData("4th")]
    public void AmenitiesAndAmountsAreNeverNames(string word)
    {
        Assert.Equal(["Kio"], Find($"DJs Kio & {char.ToUpperInvariant(word[0])}{word[1..]}!"));
        Assert.Equal(["Kio"], Find($"DJs Kio & {word}!"));
    }

    [Theory]
    [InlineData("Roxannas Red Light, Live DJs Giveaways.")] // DJs as an amenity
    [InlineData("LIVE DJ : www.twitch.tv")] // live, and no handle
    [InlineData("Gamba! 7 DJs and more")]
    [InlineData("Gamba, Raffle21m, DJs an More! Lamia")]
    [InlineData("KITTY SPEAK EZ (18+ NSFW) for Dj'S, Black jack,Strippers,VIP")]
    [InlineData("| DJs | Courts | Dynamis")]
    [InlineData("DJs-RP Bar- | Courts-")]
    [InlineData("Celebration | 7 BiS Dj's | 400mil in Games")]
    [InlineData("HOPE / POTATE / KIO | Zalera")] // no marker: they may not be performers
    [InlineData("HAPPY BIRTHDAY OPHE")]
    [InlineData("SYNC DJ | Chat and vibee")]
    [InlineData("DJs, 20m DR,20m 50/50 raffle")] // amounts are not names
    [InlineData("")]
    [InlineData("Live Djs, Prize Giveaways and inhouse gamba")] // after "live"
    [InlineData("Bar, Live DJs Prize Giveaways")]
    [InlineData("Live DJs Prize Giveaways")]
    [InlineData("Gamba! 7 DJs Nightly")] // after a number
    [InlineData("djK @ Club")] // a byline handle needs two capitals
    [InlineData("Club djayYAMS Live")] // and an @ after it
    [InlineData("Club djayYAMS")]
    [InlineData("DJ 5a!")] // a number-led name needs two letters
    [InlineData("Bard Night Kio")] // Bard Night needs a | after it
    [InlineData("Club Bard Night")]
    [InlineData("Bar DJ- Kio")] // a dash after a marker needs a name right after it
    [InlineData("Bar DJ-")]
    [InlineData("Bar, DJs")]
    [InlineData("Bar DJ")]
    [InlineData("Bar DJA")]
    public void NamesNoOneWithoutAMarker(string ad) => Assert.Empty(Find(ad));

    [Fact]
    public void DecorationEndsAName() =>
        Assert.Equal(["QUEEN BAE"], Find("Orchid House TONIGHT DJ QUEEN BAE  HAPPY BIRTHDAY OPHE bismarck"));

    [Theory]
    [InlineData("JUNGLE TEMPLE : Chaos Moogle Empy W7 P22 - DJ: www.twitch.tv/djcookie_monster", "@djcookie_monster")]
    [InlineData("Goth Friday w DJs XIANA & ETREUS! Auctions! twitch.tv/dj_etreus", "XIANA", "ETREUS@dj_etreus")] // the handle contains the name
    [InlineData("DJ Saber @ twitch.tv/0xsabrieln ♥ Seph", "Saber@0xsabrieln")] // written just before it
    [InlineData("♥Forge - Dynamis Golem Empy W11P52♥https:www.twitch.tv/dj_queenbae", "@dj_queenbae")]
    [InlineData("twitch.tv/paullyrot and https://www.twitch.tv/PaullyRot", "@paullyrot")] // the same channel once
    [InlineData("DJ Saber, DJ Rans at discord.gg/abc", "Saber", "Rans")] // other links are not streams
    [InlineData("DJ QUEEN BAE | https:www.twitch.tv/dj_queenbae", "QUEEN BAE@dj_queenbae")] // the name without spaces
    [InlineData("DJ Saber | twitch.tv/0xsabrieln", "Saber", "@0xsabrieln")] // only an @ ties a name before it
    [InlineData("DJ Saber, DJ Rans @ twitch.tv/0xsabrieln", "Saber", "Rans@0xsabrieln")] // the name just before it
    [InlineData("djSaber @ twitch.tv/0xsabrieln", "djSaber@0xsabrieln")]
    [InlineData("twitch.tv/abc DJ Saber", "Saber", "@abc")]
    public void TiesTwitchChannelsToNames(string ad, params string[] expected) => Assert.Equal(expected, Find(ad));

    [Theory]
    [InlineData("https://www.twitch.tv/blaqmagiq00", "blaqmagiq00")]
    [InlineData("https://twitch.tv/dj_pru/", "dj_pru")]
    [InlineData("https://www.twitch.tv/", null)]
    [InlineData("https://www.twitch.tv/a.b", null)]
    [InlineData("https://nottwitch.tv/x", null)]
    [InlineData("https://kick.com/x", null)]
    public void HandleOfReadsTwitchChannels(string uri, string? expected) => Assert.Equal(expected, Performers.HandleOf(new Uri(uri)));

    [Fact]
    public void MergeListsEachPerformerOnce()
    {
        static WebLink Twitch(string handle) => new($"twitch.tv/{handle}", new Uri($"https://twitch.tv/{handle}"));
        var merged = Performers.Merge(
        [
            [new Performer("IANNA", null), new Performer(null, Twitch("dj_queenbae"))],
            [new Performer("ianna", Twitch("ianna_live")), new Performer("QUEEN BAE", null)],
            [new Performer("QUEEN BAE", Twitch("dj_queenbae")), new Performer(null, Twitch("dj_queenbae")), new Performer(null, Twitch("kio"))],
            [new Performer(null, Twitch("KIO"))],
            [new Performer("IANNA", null), new Performer("QUEEN BAE", Twitch("other"))], // a stream kept is never lost or replaced
        ]);

        Assert.Equal(
            ["IANNA@ianna_live", "QUEEN BAE@dj_queenbae", "@kio"],
            merged.Select(p => $"{p.Name}@{Performers.HandleOf(p.Twitch!.Uri)}"));
    }

    [Fact]
    public void ShownIsTheNameElseTheHandle()
    {
        var link = new WebLink("twitch.tv/kio", new Uri("https://twitch.tv/kio"));
        Assert.Equal("KIO", new Performer("KIO", link).Shown);
        Assert.Equal("kio", new Performer(null, link).Shown);
    }

    [Fact]
    public void ALongListStaysFast()
    {
        // Linear in the text's length: every word here is a marker or a name
        var ad = string.Concat(Enumerable.Repeat("DJ Ab, Cd, ", 20_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.NotEmpty(Find(ad));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }
}

/// <summary>Checks <see cref="Performers.Find"/> against the corpus's hand-labelled performers.</summary>
public class PerformersCorpusTests
{
    // Ads read differently from their labels, each with the reason. A new entry means a rule is probably wrong.
    private static readonly Dictionary<string, string> KnownMisses = new()
    {
        ["pf-0189"] = "\"IANNA THOT BAE AND PHILIP\": a list with no marker; the venue's other ads call them DJs",
    };

    private static readonly Dictionary<string, (string Text, string[] Expected)> Ads = Load();

    private static Dictionary<string, (string, string[])> Load()
    {
        var ads = new Dictionary<string, (string, string[])>();
        foreach (var (id, root) in PfCorpus.Ads())
        {
            var expected = root.GetProperty("labels").GetProperty("people").EnumerateArray()
                .Where(p => p.GetProperty("role").GetString() == "performer")
                .Select(p => Key(p.GetProperty("name").GetString(), p.GetProperty("twitch").GetString()))
                .Order().ToArray();
            ads[id] = (root.GetProperty("text").GetString()!, expected);
        }

        return ads;
    }

    private static string Key(string? name, string? handle) => $"{name?.ToLowerInvariant()}@{handle?.ToLowerInvariant()}";

    private static string[] Actual(string text) =>
        [.. Performers.Find(text, WebLinks.Find(text)).Select(p => Key(p.Name, p.Twitch is null ? null : Performers.HandleOf(p.Twitch.Uri))).Order()];

    public static TheoryData<string> AllAds() => [.. Ads.Keys];

    [Theory]
    [MemberData(nameof(AllAds))]
    public void PerformersMatchTheLabels(string id)
    {
        var (text, expected) = Ads[id];
        var actual = Actual(text);
        var matches = actual.SequenceEqual(expected);
        if (KnownMisses.ContainsKey(id))
            Assert.False(matches, $"{id} now reads correctly: remove it from KnownMisses");
        else
            Assert.True(matches, $"{id}: \"{PartyFinderText.Normalise(text, true).Text}\"\n  expected: {string.Join(" ; ", expected)}\n  actual:   {string.Join(" ; ", actual)}");
    }

    [Fact]
    public void PrecisionIsAtLeast95Percent()
    {
        int right = 0, found = 0;
        foreach (var (text, expected) in Ads.Values)
        {
            var actual = Actual(text);
            found += actual.Length;
            right += actual.Count(expected.Contains);
        }

        Assert.True(found > 60, $"only {found} performers found");
        Assert.True(right >= 0.95 * found, $"{right} of {found} right");
    }
}
