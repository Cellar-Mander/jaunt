namespace Jaunt.Core.Text;

/// <summary>
/// Reads opening times from <see cref="PartyFinderText.Normalise">normalised</see> Party Finder ad
/// text: single times and ranges, closing times, written time zones, and Japanese 時 and 〆 forms.
/// </summary>
/// <remarks>
/// Works on tokens with no regex, so the cost stays linear in the ad's length. Bare hours from 1 to 11 with no AM or PM read as evening,
/// as venue ads mean them ("8-12" is 20:00 to 00:00), except in ads with Japanese text, which use
/// the 24-hour clock.
/// </remarks>
public static partial class OpeningTimes
{
    private enum Kind { Number, Word, Cjk, Symbol }

    private readonly record struct Token(Kind Kind, int Start, int End, string Text);

    /// <summary>Reads the openings in one ad.</summary>
    /// <param name="text">The ad's text after <see cref="PartyFinderText.Normalise"/>.</param>
    public static IReadOnlyList<OpeningTime> Parse(string text)
    {
        var tokens = Tokenise(text);
        var twentyFourHour = tokens.Any(t => t.Kind == Kind.Cjk);
        var spans = FindSpans(text, tokens);

        var result = new List<OpeningTime>();
        foreach (var span in spans.Where(s => s.Start is not null))
        {
            var (start, end) = To24(span.Start!.Value, span.End, twentyFourHour);
            result.Add(new OpeningTime(false, false, OpeningDay.None, null, null, start, end, span.Zone));
        }

        foreach (var span in spans.Where(s => s.Start is null))
            result.Add(new OpeningTime(false, false, OpeningDay.None, null, null, null, Single(span.End!.Value, twentyFourHour), span.Zone));

        return result;
    }

    private static List<Token> Tokenise(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var start = i;
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            Kind kind;
            if (char.IsAsciiDigit(c))
            {
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                    i++;
                kind = Kind.Number;
            }
            else if (IsCjk(c))
            {
                i++;
                kind = Kind.Cjk;
            }
            else if (char.IsLetter(c))
            {
                while (i < text.Length && char.IsLetter(text[i]) && !IsCjk(text[i]))
                    i++;
                kind = Kind.Word;
            }
            else
            {
                i++;
                kind = Kind.Symbol;
            }

            tokens.Add(new Token(kind, start, i, text[start..i]));
        }

        return tokens;
    }

    /// <summary>
    /// Kana, kanji and the marks used with them, such as 〆 and ー. The katakana middle dot ・ is
    /// left out: English ads use it as a separator.
    /// </summary>
    private static bool IsCjk(char c) =>
        c is (>= '぀' and <= 'ヺ') or 'ー' or 'ヽ' or 'ヾ' or 'ヿ' or (>= '㐀' and <= '䶿') or (>= '一' and <= '鿿')
            or (>= '豈' and <= '﫿') or '々' or '〆';

    private static bool Adjacent(Token a, Token b) => a.End == b.Start;

    private static bool IsSymbol(Token t, char c) => t.Kind == Kind.Symbol && t.Text[0] == c;

    private static bool IsWord(Token t, string w) => t.Kind == Kind.Word && t.Text.Equals(w, StringComparison.OrdinalIgnoreCase);

    private static Token? At(List<Token> tokens, int i) => i >= 0 && i < tokens.Count ? tokens[i] : null;

    /// <summary>
    /// Whether the token at <paramref name="i"/> runs on from a letter, digit or one of <c>:/.</c>
    /// before it, as in W30, P:43 or discord.gg/7F. Kana and kanji don't count: 本日23時 is a time.
    /// </summary>
    private static bool JoinedToPrevious(List<Token> tokens, int i)
    {
        if (At(tokens, i - 1) is not { } prev || !Adjacent(prev, tokens[i]))
            return false;
        return prev.Kind is Kind.Word or Kind.Number || (prev.Kind == Kind.Symbol && prev.Text[0] is ':' or '/' or '.');
    }
}
