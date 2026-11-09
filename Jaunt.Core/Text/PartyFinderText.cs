using System.Globalization;
using System.Text;

namespace Jaunt.Core.Text;

/// <summary>Ad text with the game's glyphs decoded, and where the boxed text was.</summary>
/// <param name="Text">The normalised text.</param>
/// <param name="Boxed">
/// The runs of <see cref="Text"/> that were written in boxed letters or digits, in order. Ads often
/// mark a venue's name this way.
/// </param>
public sealed record NormalisedText(string Text, IReadOnlyList<Range> Boxed);

/// <summary>
/// Turns Party Finder ad text into plain text the parsers can read: the game's private-use glyphs
/// become the letters, digits or words they stand for, decoration becomes spaces, and the result is
/// NFKC with runs of spaces collapsed.
/// </summary>
/// <remarks>
/// Glyph values are Dalamud's <c>SeIconChar</c>. <c>U+E032</c>, <c>U+E05F</c> and <c>U+E0B0</c> are
/// not in it; they were identified from the game's font. Case is kept, so callers fold it. Runs in
/// one pass over the text plus NFKC, so the cost stays linear in the ad's length.
/// </remarks>
public static class PartyFinderText
{
    private const int PrivateUseFirst = 0xE000;
    private const int PrivateUseLast = 0xF8FF;
    private const int BoxedNumber10 = 0xE099;
    private const int BoxedNumber31 = 0xE0AE;

    /// <summary>Normalises one ad's text.</summary>
    public static NormalisedText Normalise(string raw)
    {
        var decoded = Decode(raw);
        var composed = Compose(decoded);
        return Collapse(composed);
    }

    /// <summary>Text with a flag per char saying whether it came from a boxed glyph.</summary>
    private sealed class Marked
    {
        public StringBuilder Text { get; } = new();
        public List<bool> Boxed { get; } = [];

        public void Append(char c, bool boxed)
        {
            Text.Append(c);
            Boxed.Add(boxed);
        }

        public void Append(string s, bool boxed)
        {
            foreach (var c in s)
                Append(c, boxed);
        }

        /// <summary>Whitespace. Its flag is never read, since <see cref="Collapse"/> drops it first.</summary>
        // Stryker disable once Boolean : Collapse never reads a whitespace char's flag
        public void Space(char c) => Append(c, false);

        public char? Last => Text.Length > 0 ? Text[^1] : null;

        /// <summary>Whether the last char is boxed. Only read after <see cref="Last"/> is checked.</summary>
        public bool LastBoxed => Boxed[^1];
    }

    private static Marked Decode(string raw)
    {
        var output = new Marked();
        var runes = raw.EnumerateRunes().ToArray();
        for (var i = 0; i < runes.Length; i++)
        {
            var r = runes[i];
            var o = r.Value;
            var next = i + 1 < runes.Length ? runes[i + 1] : default; // U+0000 is neither letter nor glyph

            if (Boxed(o) is { } boxed)
            {
                // "7" then boxed 30 is how ads write 7:30: there is no glyph for :30.
                if (o is >= BoxedNumber10 and <= BoxedNumber31 && output.Last is { } prev && char.IsAsciiDigit(prev))
                    output.Append(':', output.LastBoxed);
                output.Append(boxed, true);
            }
            else if (Plain(o) is { } plain)
            {
                output.Append(plain, false);
            }
            else if (LetterLike(o) is { } letter)
            {
                // Only a letter when it touches one; on its own it is decoration.
                if (output.Last is { } p && char.IsLetter(p))
                    output.Append(char.IsUpper(p) ? letter : char.ToLowerInvariant(letter), output.LastBoxed);
                else if (IsLetterOrLetterGlyph(next))
                    output.Append(Rune.IsLower(next) ? char.ToLowerInvariant(letter) : letter, Boxed(next.Value) is not null);
                else
                    output.Space(' ');
            }
            else if (Word(o) is { } word)
            {
                output.Append(word, false);
                // "10PM" stays attached, but "PM" must not run into a following word or number.
                if (Rune.IsLetterOrDigit(next) || Boxed(next.Value) is not null)
                    output.Space(' ');
            }
            else if (o == '\r' || o == 0xE035) // ArrowDown is used as a line break
            {
                output.Space('\n');
            }
            else if (IsDecoration(r))
            {
                output.Space(' ');
            }
            else
            {
                output.Append(r.ToString(), false);
            }
        }

        return output;
    }

    /// <summary>A boxed letter, digit or sign: the text it reads as.</summary>
    private static string? Boxed(int o) => o switch
    {
        >= 0xE071 and <= 0xE08A => ((char)('A' + o - 0xE071)).ToString(), // BoxedLetterA..Z
        0xE0B0 => "E", // boxed E, not in SeIconChar
        >= 0xE08F and <= 0xE0AE => (o - 0xE08F).ToString(CultureInfo.InvariantCulture), // BoxedNumber0..31
        >= 0xE0E0 and <= 0xE0E9 => ((char)('0' + o - 0xE0E0)).ToString(), // BoxedOutlinedNumber0..9
        0xE070 => "?", // BoxedQuestionMark
        0xE0AF => "+", // BoxedPlus
        >= 0xE0C1 and <= 0xE0C6 => RomanNumerals[o - 0xE0C1], // BoxedRoman1..6
        _ => null,
    };

    private static readonly string[] RomanNumerals = ["I", "II", "III", "IV", "V", "VI"];

    /// <summary>An unboxed digit or sign glyph.</summary>
    private static char? Plain(int o) => o switch
    {
        >= 0xE060 and <= 0xE069 => (char)('0' + o - 0xE060), // Number0..9
        >= 0xE0B1 and <= 0xE0B9 => (char)('1' + o - 0xE0B1), // Instance1..9
        0xE04E => '+', // GlamouredDyed, drawn as a plus
        _ => null,
    };

    /// <summary>A glyph that ads use as a letter when it sits inside a word.</summary>
    private static char? LetterLike(int o) => o switch
    {
        0xE04A => 'O', // Circle
        0xE05C => 'O', // Buff: an o in a house shape
        0xE05F => 'X', // an x in a box, not in SeIconChar
        0xE032 => 'V', // a V-shaped chevron, not in SeIconChar
        _ => null,
    };

    /// <summary>Time glyphs, as words. AM and PM stay attached to the number before them.</summary>
    private static string? Word(int o) => o switch
    {
        0xE06D => "AM", // TimeAm
        0xE06E => "PM", // TimePm
        0xE06B => " ST ", // LevelDe, drawn "St" and used for server time
        // Local, Server, Eorzea repeat in that order; 0xE0D0 is a multiple of 3, so o % 3 picks one.
        >= 0xE0D0 and <= 0xE0DB => (o % 3) switch // Local/Server/EorzeaTime En, De, Fr, Ja
        {
            0 => " LT ",
            1 => " ST ",
            _ => " ET ",
        },
        _ => null,
    };

    private static bool IsLetterOrLetterGlyph(Rune r) =>
        Rune.IsLetter(r) || (Boxed(r.Value) is { } b && char.IsLetter(b[0])) || LetterLike(r.Value) is not null;

    /// <summary>Symbols ads use as decoration or separators: stars, hearts, notes, arrows, bullets.</summary>
    private static bool IsDecoration(Rune r)
    {
        var o = r.Value;
        if (o is >= PrivateUseFirst and <= PrivateUseLast)
            return true; // every other game glyph
        if (o is >= 0x2190 and <= 0x21FF)
            return true; // arrows
        if (o is 0x2022 or 0x2020 or 0x2021 or 0x203B or 0x2234 or 0x2235)
            return true; // • † ‡ ※ ∴ ∵
        return Rune.GetUnicodeCategory(r) == UnicodeCategory.OtherSymbol;
    }

    /// <summary>
    /// NFKC, one run of equal flags at a time so the flags stay aligned. Boxed runs are ASCII, which
    /// NFKC leaves as it is.
    /// </summary>
    private static Marked Compose(Marked decoded)
    {
        var output = new Marked();
        var text = decoded.Text.ToString();
        var start = 0;
        while (start < text.Length)
        {
            var boxed = decoded.Boxed[start];
            var end = start;
            while (end < text.Length && decoded.Boxed[end] == boxed)
                end++;
            var run = text[start..end];
            output.Append(run.Normalize(NormalizationForm.FormKC), boxed);
            start = end;
        }

        return output;
    }

    /// <summary>
    /// Collapses each run of whitespace to one space, or to one newline when it holds a line break,
    /// and trims both ends.
    /// </summary>
    private static NormalisedText Collapse(Marked composed)
    {
        var text = new StringBuilder(composed.Text.Length);
        var ranges = new List<Range>();
        // Stryker disable once Boolean : leading whitespace is dropped whatever these start as
        bool pending = false, pendingNewline = false;
        var boxedStart = -1;

        for (var i = 0; i < composed.Text.Length; i++)
        {
            var c = composed.Text[i];
            if (char.IsWhiteSpace(c))
            {
                pending = true;
                pendingNewline |= c == '\n';
                continue;
            }

            if (pending && text.Length > 0)
            {
                CloseBoxed(ranges, ref boxedStart, text.Length);
                text.Append(pendingNewline ? '\n' : ' ');
            }

            pending = pendingNewline = false;
            if (composed.Boxed[i])
            {
                if (boxedStart < 0)
                    boxedStart = text.Length;
            }
            else
            {
                CloseBoxed(ranges, ref boxedStart, text.Length);
            }

            text.Append(c);
        }

        CloseBoxed(ranges, ref boxedStart, text.Length);
        return new NormalisedText(text.ToString(), ranges);
    }

    private static void CloseBoxed(List<Range> ranges, ref int boxedStart, int end)
    {
        if (boxedStart < 0)
            return;
        ranges.Add(boxedStart..end);
        boxedStart = -1;
    }
}
