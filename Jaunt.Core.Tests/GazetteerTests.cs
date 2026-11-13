using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class AddressTokenizerTests
{
    private static string T(string text) => string.Join(' ', AddressTokenizer
        .Tokenize(text, TestGazetteer.Instance.CjkWords, TestGazetteer.Instance.MaxCjkWordLength)
        .Select(t => $"{t.Gap.Trim()}{t.Text}")).TrimEnd();

    [Theory]
    [InlineData("HypMist W18P35", "hyp mist w 18 p 35")] // case change and letter–digit boundaries
    [InlineData("djayYAMS", "djay yams")]
    [InlineData("CLUBNoir", "clubnoir")] // upper to lower is not a boundary
    [InlineData("Raiden-LB-3-28", "raiden -lb -3 -28")]
    [InlineData("10ー58", "10 -58")] // long-vowel mark between digits is a dash
    [InlineData("ユニコーン", "ユニコーン")] // but not inside a word
    [InlineData("マナパンデモゴブ27-10", "マナ パンデモ ゴブ 27 -10")] // split on known names
    [InlineData("アスララベ", "アスラ ラベ")]
    [InlineData("ラベンダーベッド", "ラベンダーベッド")] // the longest name wins
    [InlineData("別館ミスト", "別 館 ミスト")] // unknown characters one at a time
    [InlineData("a)", "a )")] // trailing separators end up on the end token
    public void Splits(string text, string expected) => Assert.Equal(expected, T(text));

    [Fact]
    public void NumbersCarryTheirValueUnlessTooLong()
    {
        var tokens = AddressTokenizer.Tokenize("007 123456 1234567", null, 0);

        Assert.Equal([7, 123456, -1, 0], tokens.Select(t => t.Value));
        Assert.Equal("007", tokens[0].Raw);
        var lavB = AddressTokenizer.Tokenize("LavB", null, 0)[1];
        Assert.Equal(("b", "B"), (lavB.Text, lavB.Raw));
    }
}

public class GazetteerTests
{
    private static readonly Gazetteer G = TestGazetteer.Instance;

    [Theory]
    [InlineData("the lavender beds", District.LavenderBeds)] // from the game's data
    [InlineData("ミスト ヴィレッジ", District.Mist)] // "ミスト・ヴィレッジ" spans two runs
    [InlineData("lav b", District.LavenderBeds)] // community shorthand
    [InlineData("ゴブ", District.Goblet)]
    [InlineData("gobby", District.Goblet)]
    public void DistrictNames(string key, District expected) => Assert.Equal(expected, G.Find(key)?.District);

    [Theory]
    [InlineData("kraken", "Kraken", true)]
    [InlineData("パンデモニウム", "Pandaemonium", true)] // Japanese full name
    [InlineData("パンデモ", "Pandaemonium", false)] // a short form is not a full name
    [InlineData("exl", "Excalibur", false)]
    [InlineData("gobby", "Goblin", false)] // both a world and a district
    public void WorldNames(string key, string world, bool fullName)
    {
        var entry = G.Find(key);

        Assert.Equal(world, Assert.Single(entry!.Worlds).Name);
        Assert.Equal(fullName, entry.FullName);
    }

    [Theory]
    [InlineData("dynamis")]
    [InlineData("dyn")]
    [InlineData("メテオ")]
    public void DataCenters(string key) => Assert.True(G.Find(key)?.DataCenter);

    [Theory]
    [InlineData("krak", "NA", "Kraken")]
    [InlineData("KRAK", "NA", "Kraken")]
    [InlineData("ult", "NA", "Ultros")] // unique in the region
    [InlineData("ult", "JP", "Ultima")]
    [InlineData("ult", null, null)] // two anywhere
    [InlineData("ult", "EU", null)] // none in the region, two elsewhere
    [InlineData("sagi", "NA", "Sagittarius")] // none in the region, one elsewhere
    [InlineData("ma", "NA", null)] // too short
    [InlineData("gil", "NA", null)] // a common word
    [InlineData("gilg", "NA", "Gilgamesh")]
    [InlineData("krakenx", "NA", null)] // longer than the name
    public void Prefixes(string word, string? region, string? expected) =>
        Assert.Equal(expected, G.WorldByPrefix(word, region)?.Name);

    [Fact]
    public void AliasesForWorldsNotInTheDataAreDropped()
    {
        var g = new Gazetteer([new GameWorld("Kraken", "Dynamis", "NA")], []);

        Assert.Null(g.Find("パンデモ"));
        Assert.Equal("Kraken", g.FindWorld("KRAKEN")?.Name);
        Assert.Null(g.FindWorld(null));
    }
}
