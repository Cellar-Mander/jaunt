using Jaunt.Core.Display;

namespace Jaunt.Core.Tests;

public class FontFallbackTests
{
    /// <summary>
    /// The game font's symbols as listed in-game (2026-11-14), plus printable ASCII, the katakana the
    /// tests use, and ʜ and ʀ, which FontAwesome icons occupy in the plugin's default font.
    /// </summary>
    private static readonly HashSet<int> GameFont = Build(
        " π™′＾¿‰øØ×∞∩£¥¢Ð€ªº†‡¤ŒœŠšŸÅωψ↑↓→←⇔⇒♂♀♪¶§±＜＞≥≤≡÷½¼¾©®¹²³※｢｣«»≪≫《》【】℉℃。·•‥…¨°"
        + "╲╳╱☁☀☃♭♯✓〃●◎○■□▲△▼▽∇♥♡★☆◆◇♦♣♠♤♧αß∇ΘΦΩδ∂∃∀∈∋∑√∝∠∟∥∪∨∧∫∮∬∴∵∽≒≠≦≧⊂⊃⊆⊇⊥⊿⌒─━│┃¦"
        + "０１２３４５６７８９ＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺＬƎワンʜʀ");

    private static HashSet<int> Build(string extra)
    {
        var set = Enumerable.Range(0x20, 0x5F).ToHashSet();
        foreach (var rune in extra.EnumerateRunes())
            set.Add(rune.Value);
        return set;
    }

    private static string Fold(string text) => FontFallback.Fold(text, GameFont.Contains);

    [Theory]
    [InlineData("꧁Tʜᴇ Aᴜʀᴏʀᴀ Tʜᴇᴀᴛᴇʀ꧂", "«The Aurora Theater»")]
    [InlineData("✦✦✦ 🌴 DESERT ROSE 🌴 ✦✦✦", "◆◆◆ DESERT ROSE ◆◆◆")]
    [InlineData("THE LƎVE⅃", "THE LƎVEL")]
    [InlineData("𝗛𝗢𝗪𝗟  ワン", "HOWL  ワン")]
    [InlineData("ᗰYᖇᖇᕼᗩ", "MYRRHA")]
    public void VenueNamesTheFontCantDrawBecomeReadable(string name, string shown) => Assert.Equal(shown, Fold(name));

    [Fact]
    public void TextTheFontDrawsIsReturnedAsItIs()
    {
        const string name = "♥ Crafty Cafe ♥  【Ｌounge】";

        Assert.Same(name, Fold(name));
    }

    [Theory]
    [InlineData("ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘꞯʀꜱᴛᴜᴠᴡʏᴢ", "abcdefghijklmnopqrstuvwyz")]
    [InlineData("Sᴍᴀʟʟ Cᴀᴘs", "Small Caps")]
    public void SmallCapitalsAreLowerCase(string text, string shown) => Assert.Equal(shown, Fold(text));

    [Fact]
    public void SmallCapitalsAreLowerCaseEvenWhenTheFontHasThem()
    {
        var everything = (Func<int, bool>)(_ => true);

        Assert.Equal("the", FontFallback.Fold("ᴛʜᴇ", everything));
    }

    [Theory]
    [InlineData("ᗩᗷᑕᗪᗴᖴᕼᒍᒪᗰᑎᑭᑫᖇᔕᑌᐯᗯ", "ABCDEFHJLMNPQRSUVW")]
    [InlineData("⅃Ǝ", "LE")]
    [InlineData("✦✧✩✪✫✬✭✮✯✰", "◆◇☆★★★★★★☆")]
    [InlineData("❤꧁꧂", "♥«»")]
    public void LookAlikesStandInForCharactersTheFontLacks(string text, string shown) =>
        Assert.Equal(shown, FontFallback.Fold(text, Build("◆◇☆★♥«»").Contains));

    [Fact]
    public void ACharacterTheFontHasIsKeptEvenIfItHasALookAlike()
    {
        var withStars = Build("✦ᗩ⅃");

        Assert.Equal("✦ᗩ⅃", FontFallback.Fold("✦ᗩ⅃", withStars.Contains));
    }

    [Fact]
    public void ALookAlikeTheFontAlsoLacksIsLeftOut() =>
        Assert.Equal("Star", FontFallback.Fold("✦ Star", Build("").Contains));

    [Theory]
    [InlineData("𝐁𝐨𝐥𝐝 𝘐𝘵𝘢𝘭𝘪𝘤 𝔉𝔯𝔞𝔨𝔱𝔲𝔯 𝟙𝟚𝟛", "Bold Italic Fraktur 123")]
    [InlineData("𝗛i", "Hi")]
    [InlineData("a𝗛", "aH")]
    [InlineData("ｂａｒ", "bar")] // full-width letters the font lacks
    public void StyledLettersBecomePlainOnes(string text, string shown) => Assert.Equal(shown, Fold(text));

    [Fact]
    public void ACompatibilityFormWhosePlainFormTheFontAlsoLacksIsLeftOut() =>
        Assert.Equal("Bar", Fold("Bar ㍿")); // ㍿ decomposes to kanji the test font lacks

    [Theory]
    [InlineData("🌴 Bar", "Bar")]
    [InlineData("Bar 🌴", "Bar")]
    [InlineData("Bar 🌴 🌴 Grill", "Bar Grill")]
    [InlineData("Bar🌴Grill", "BarGrill")]
    [InlineData("🌴", "")]
    [InlineData("Bar  Grill 🌴", "Bar Grill")] // once something is left out, double spaces close up too
    public void CharactersWithNoStandInAreLeftOutAndTheirSpacesClosed(string text, string shown) =>
        Assert.Equal(shown, Fold(text));

    [Fact]
    public void EmojiJoinersAndVariationSelectorsAreLeftOut() =>
        Assert.Equal("♥ Bar", Fold("♥️ Bar‍"));

    [Fact]
    public void WhitespaceIsKeptWhetherOrNotTheFontHasIt() =>
        Assert.Equal("Bar\nGrill\ta", Fold("Bar\nGrill\tᴀ"));

    [Fact]
    public void ALoneSurrogateIsLeftOut() => Assert.Equal("Bar", Fold("Bar\uD83C"));
}
