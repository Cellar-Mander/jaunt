using System.Text.Json;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class PartyFinderTextTests
{
    private static string N(string raw) => PartyFinderText.Normalise(raw).Text;

    [Theory]
    [InlineData("", "NOIR")] // BoxedLetter A..Z, both ends of the range
    [InlineData("", "AZ")]
    [InlineData("", "SECRET")] // U+E0B0 is a boxed E (pf-0045)
    [InlineData("W P", "W4 P35")] // Number0..9 (pf-0019)
    [InlineData("", "09")]
    [InlineData("W", "W09")] // BoxedNumber0..9
    [InlineData("(+ NSFW)", "(18+ NSFW)")] // BoxedNumber18 (pf-0355)
    [InlineData(" ", "10 31")]
    [InlineData("", "09")] // BoxedOutlinedNumber0..9
    [InlineData("", "19")] // Instance1..9
    [InlineData("", "?+")] // BoxedQuestionMark, BoxedPlus
    [InlineData("     ", "I II III IV V VI")] // BoxedRoman1..6
    [InlineData("", "ALT+TAB")] // GlamouredDyed is a plus (pf-0216)
    public void GlyphsReadAsLettersAndDigits(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Theory]
    [InlineData("MONDAYS   |", "MONDAYS 7:30PM ET |")] // pf-0242
    [InlineData("9", "9:10")]
    [InlineData("9", "9:31")]
    [InlineData("+", "18+")] // no digit before it
    [InlineData("W ", "W 18")]
    [InlineData("", "19")] // two one-digit glyphs join without a colon
    [InlineData("1", "1II")] // Roman numerals are not minutes
    public void ColonGoesBetweenADigitAndATwoDigitGlyph(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Theory]
    [InlineData("9-12 ET!", "9PM-12AM ET!")] // pf-0180
    [InlineData("10EST", "10PM EST")]
    [InlineData("102", "10PM 2")]
    [InlineData("10", "10PM A")]
    [InlineData("@ 16 → Lich", "@ 16 ST Lich")] // pf-0002
    [InlineData("|", "17 ST |")]
    [InlineData("@17 |", "@17 ST |")] // LevelDe used for ST (pf-0016)
    [InlineData("", "LT ET")]
    [InlineData("", "LT ST ET")] // German
    [InlineData("", "LT ST ET")] // Japanese
    public void TimeGlyphsReadAsWords(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Theory]
    [InlineData("", "MON")] // Circle between boxed letters (pf-0236)
    [InlineData("", "VALK")] // V glyph before boxed letters (pf-0277)
    [InlineData("P h ie", "P hox ie")] // Buff and x glyph after a lower-case letter (pf-0230)
    [InlineData("ie", "xie")]
    [InlineData("A", "AO")]
    [InlineData("a  b", "a b")] // on its own it is decoration
    [InlineData("", "")]
    [InlineData("12", "1 2")]
    public void LetterLikeGlyphsAreLettersOnlyInsideAWord(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Theory]
    [InlineData("PILLOWTALK ‹‹  24/7  Raiden", "PILLOWTALK ‹‹ 24/7 Raiden")] // Debuff
    [InlineData("VIBES Dynamis", "VIBES Dynamis")] // BoxedStar
    [InlineData("BINGO • •DRINKS", "BINGO DRINKS")] // bullet and Dice
    [InlineData("★Club♥Noir♪", "Club Noir")]
    [InlineData("a←b⇿c", "a b c")] // both ends of the arrows block
    [InlineData("ab", "a b")] // first private-use code point
    [InlineData("†Chaos ※ a ∴∵", "Chaos a")]
    [InlineData("est[P", "est [P")] // U+E05A, the small dots glyph
    [InlineData("abc", "a b c")] // unknown game glyphs
    [InlineData("a\U0001F496b", "a b")] // emoji outside the BMP
    [InlineData("a+b<c=d~e|f", "a+b<c=d~e|f")] // ASCII signs stay
    public void DecorationBecomesSpace(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Theory]
    [InlineData("a  \t b", "a b")]
    [InlineData("  a b  ", "a b")]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a \r  \r b", "a\nb")]
    [InlineData("Bathhouse&SpaInto RP?", "Bathhouse&Spa\nInto RP?")] // ArrowDown as a line break (pf-0032)
    [InlineData("♥\r", "")]
    [InlineData("", "")]
    public void WhitespaceCollapses(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Theory]
    [InlineData("ＣＬＵＢ　０１", "CLUB 01")] // fullwidth and ideographic space
    [InlineData("ﾊﾞｰ", "バー")] // half-width kana with voiced mark composes
    [InlineData("｢名｣", "「名」")]
    [InlineData("20時～24時", "20時~24時")]
    [InlineData("Ⅴ", "V")]
    public void UnboxedTextIsNfkc(string raw, string expected) => Assert.Equal(expected, N(raw));

    [Fact]
    public void BoxedRunsAreReported()
    {
        // ⟦CLUB⟧ ⟦NOIR⟧ in Lich
        var result = PartyFinderText.Normalise("  in Lich");

        Assert.Equal("CLUB NOIR in Lich", result.Text);
        Assert.Equal(["CLUB", "NOIR"], result.Boxed.Select(r => result.Text[r]));
    }

    [Fact]
    public void BoxedRunsSurviveNfkcAndCollapsingBeforeThem()
    {
        var result = PartyFinderText.Normalise("Ａ　　★ ");

        Assert.Equal("A AB18", result.Text);
        Assert.Equal(["AB18"], result.Boxed.Select(r => result.Text[r]));
    }

    [Theory]
    [InlineData("", new[] { "MON" })] // a letter glyph inside a boxed run joins it
    [InlineData("", new[] { "VA" })]
    [InlineData("h", new string[0])]
    [InlineData("9", new[] { "30" })] // the colon is not boxed after a plain digit
    [InlineData("", new[] { "1:30" })] // but is between boxed digits
    [InlineData("\nx", new[] { "A", "B" })]
    [InlineData("c", new[] { "AB" })] // an unboxed letter ends the run
    [InlineData("W", new string[0])] // Number and Instance digits are not boxed
    [InlineData("10", new string[0])] // nor are time words
    public void LetterGlyphsAndColonsTakeTheirNeighboursBoxing(string raw, string[] expected)
    {
        var result = PartyFinderText.Normalise(raw);

        Assert.Equal(expected, result.Boxed.Select(r => result.Text[r]));
    }
}

public class PartyFinderTextCorpusTests
{
    // Names the normaliser can't recover, each with the reason. Keep this list short: a new entry
    // means the glyph table or the label is probably wrong.
    private static readonly Dictionary<string, string> KnownMisses = new()
    {
        ["pf-0048"] = "LEVEL is drawn with box-drawing and maths symbols",
    };

    private static readonly Dictionary<string, (string Text, string Name)> Named = LoadNamed();

    private static Dictionary<string, (string Text, string Name)> LoadNamed()
    {
        var named = new Dictionary<string, (string, string)>();
        var path = Path.Combine(AppContext.BaseDirectory, "Corpus", "pf-listings.jsonl");
        foreach (var line in File.ReadLines(path).Where(l => l.Length > 0))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.GetProperty("labels").GetProperty("name") is { ValueKind: JsonValueKind.Object } name)
                named[root.GetProperty("id").GetString()!] = (root.GetProperty("text").GetString()!, name.GetProperty("raw").GetString()!);
        }

        return named;
    }

    public static TheoryData<string> NamedAds() => [.. Named.Keys];

    private static string NoSpaces(string s) => string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    [Theory]
    [MemberData(nameof(NamedAds))]
    public void LabelledNameIsInTheNormalisedText(string id)
    {
        // Spacing is ignored: ads space out letters ("T B U") and labels don't follow that. This
        // checks that no letter is lost or misread.
        var (text, name) = Named[id];
        var normalised = PartyFinderText.Normalise(text).Text;
        var found = NoSpaces(normalised).Contains(NoSpaces(PartyFinderText.Normalise(name).Text), StringComparison.OrdinalIgnoreCase);

        if (KnownMisses.ContainsKey(id))
            Assert.False(found, $"{id} now normalises correctly: remove it from KnownMisses");
        else
            Assert.True(found, $"{id}: \"{name}\" not in \"{normalised}\"");
    }
}
