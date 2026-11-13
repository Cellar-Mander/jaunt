using System.Globalization;
using System.Text;

namespace Jaunt.Core.Text;

internal enum TokenKind
{
    Word,
    Number,
    Cjk,

    /// <summary>Always last: holds the separators after the last word, such as a closing bracket.</summary>
    End,
}

/// <summary>A word, number or CJK word, with the separator text before it.</summary>
/// <param name="Text">Lower case for words; as written otherwise.</param>
/// <param name="Raw">As written.</param>
/// <param name="Gap">The separators between the previous token and this one; empty when they touch.</param>
/// <param name="Value">A number's value, or -1 when it is too long to be one an address uses.</param>
internal readonly record struct Token(TokenKind Kind, string Text, string Raw, string Gap, int Value);

/// <summary>
/// Splits normalised ad text into tokens. Latin runs split where a lower-case letter meets an
/// upper-case one ("HypMist" → hyp, mist) and where letters meet digits ("W30" → w, 30). Runs of other
/// scripts have no spaces, so they are split greedily on the gazetteer's words, longest first, and
/// otherwise one character at a time.
/// </summary>
internal static class AddressTokenizer
{
    private const int MaxNumberDigits = 6;

    /// <param name="words">CJK words to split runs on; null keeps each run whole.</param>
    public static List<Token> Tokenize(string text, IReadOnlySet<string>? words, int maxWordLength)
    {
        var tokens = new List<Token>();
        var gap = new StringBuilder();
        var lookup = (words as HashSet<string>)?.GetAlternateLookup<ReadOnlySpan<char>>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var start = i;
            if (IsLatinLetter(c))
            {
                while (i < text.Length && IsLatinLetter(text[i]))
                    i++;
                AddLatin(tokens, gap, text[start..i]);
            }
            else if (char.IsAsciiDigit(c))
            {
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                    i++;
                var digits = text[start..i];
                var value = digits.Length <= MaxNumberDigits ? int.Parse(digits, CultureInfo.InvariantCulture) : -1;
                Add(tokens, gap, TokenKind.Number, digits, digits, value);
            }
            else if (c == 'ー' && i > 0 && char.IsAsciiDigit(text[i - 1]) && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                gap.Append('-'); // a long-vowel mark between digits is a dash: "10ー58"
                i++;
            }
            else if (char.IsLetter(c))
            {
                while (i < text.Length && char.IsLetter(text[i]) && !IsLatinLetter(text[i]))
                    i++;
                AddRun(tokens, gap, text.AsSpan(start, i - start), lookup, maxWordLength);
            }
            else
            {
                gap.Append(c);
                i++;
            }
        }

        Add(tokens, gap, TokenKind.End, "", "");
        return tokens;
    }

    private static bool IsLatinLetter(char c) => c < 0x250 && char.IsLetter(c);

    private static void Add(List<Token> tokens, StringBuilder gap, TokenKind kind, string text, string raw, int value = 0)
    {
        tokens.Add(new Token(kind, text, raw, gap.ToString(), value));
        gap.Clear();
    }

    private static void AddLatin(List<Token> tokens, StringBuilder gap, string run)
    {
        var start = 0;
        for (var k = 1; k <= run.Length; k++)
        {
            if (k < run.Length && !(char.IsLower(run[k - 1]) && char.IsUpper(run[k])))
                continue;
            var part = run[start..k];
            Add(tokens, gap, TokenKind.Word, part.ToLowerInvariant(), part);
            start = k;
        }
    }

    private static void AddRun(
        List<Token> tokens, StringBuilder gap, ReadOnlySpan<char> run,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>>? words, int maxWordLength)
    {
        if (words is not { } lookup)
        {
            var whole = run.ToString();
            Add(tokens, gap, TokenKind.Cjk, whole, whole);
            return;
        }

        var pos = 0;
        while (pos < run.Length)
        {
            var length = 1; // an unknown character is a token of its own
            for (var l = Math.Min(maxWordLength, run.Length - pos); l > 1; l--)
            {
                if (lookup.Contains(run.Slice(pos, l)))
                {
                    length = l;
                    break;
                }
            }

            var word = run.Slice(pos, length).ToString();
            Add(tokens, gap, TokenKind.Cjk, word, word);
            pos += length;
        }
    }
}
