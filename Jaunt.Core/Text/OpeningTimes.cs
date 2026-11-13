using System.Diagnostics.CodeAnalysis;

namespace Jaunt.Core.Text;

/// <summary>
/// Reads opening times from <see cref="PartyFinderText.Normalise">normalised</see> Party Finder ad
/// text: "open now", 24/7, days, dates, single times and ranges, written time zones, and Japanese
/// 時 and 〆 forms.
/// </summary>
/// <remarks>
/// Works on tokens in one pass each for dates, times, days and "open now", with no regex, so the
/// cost stays linear in the ad's length. Bare hours from 1 to 11 with no AM or PM read as evening,
/// as venue ads mean them ("8-12" is 20:00 to 00:00), except in ads with Japanese text, which use
/// the 24-hour clock.
/// </remarks>
public static partial class OpeningTimes
{
    private enum Kind { Number, Word, Cjk, Symbol }

    private readonly record struct Token(Kind Kind, int Start, int End, string Text);

    /// <summary>Reads the openings in one ad.</summary>
    /// <param name="text">The ad's text after <see cref="PartyFinderText.Normalise"/>.</param>
    /// <param name="seen">The date the ad was seen, which picks the year and reading of a date like 7/11.</param>
    public static IReadOnlyList<OpeningTime> Parse(string text, DateOnly seen)
    {
        var tokens = Tokenise(text);
        var used = new bool[tokens.Count];
        var twentyFourHour = tokens.Any(t => t.Kind == Kind.Cjk);

        var always = FindAlways(tokens, used);
        var date = FindDate(tokens, used, seen);
        var spans = FindSpans(text, tokens, used);
        var (adDay, weekdays) = FindDays(text, tokens, spans);
        var now = FindNow(text, tokens, spans);

        var result = new List<OpeningTime>();
        var endOnly = spans.Where(s => s.Start is null).ToList();
        if (now)
        {
            var closing = endOnly.FirstOrDefault();
            if (closing is not null)
                endOnly.Remove(closing);
            result.Add(new OpeningTime(true, false, OpeningDay.None, null, null, null, Time(closing?.End), closing?.Zone));
        }

        if (always)
            result.Add(new OpeningTime(false, true, OpeningDay.None, null, null, null, null, null));

        foreach (var span in spans.Where(s => s.Start is not null))
        {
            var hasWeekday = weekdays.TryGetValue(span, out var w);
            var (day, weekday) = date is not null ? (OpeningDay.Date, (DayOfWeek?)null)
                : hasWeekday ? (OpeningDay.Weekday, w)
                : (adDay, null);
            var (start, end) = To24(span.Start!.Value, span.End, twentyFourHour);
            result.Add(new OpeningTime(false, false, day, weekday, date, start, end, span.Zone));
        }

        foreach (var span in endOnly)
        {
            var day = date is not null ? OpeningDay.Date : adDay;
            result.Add(new OpeningTime(false, false, day, null, date, null, Single(span.End!.Value, twentyFourHour), span.Zone));
        }

        if (result.Count == 0 && (date is not null || adDay != OpeningDay.None))
            result.Add(new OpeningTime(false, false, date is not null ? OpeningDay.Date : adDay, null, date, null, null, null));

        return result;

        TimeOnly? Time(Clock? c) => c.HasValue ? Single(c.Value, twentyFourHour) : null;
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

    private static bool IsSymbol([NotNullWhen(true)] Token? t, char c) => t is { Kind: Kind.Symbol } && t.Value.Text[0] == c;

    private static bool IsWord([NotNullWhen(true)] Token? t, string w) => t is { Kind: Kind.Word } && t.Value.Text.Equals(w, StringComparison.OrdinalIgnoreCase);

    private static bool IsWordIn([NotNullWhen(true)] Token? t, HashSet<string> words) => t is { Kind: Kind.Word } && words.Contains(t.Value.Text);

    private static Token? At(List<Token> tokens, int i) => i >= 0 && i < tokens.Count ? tokens[i] : null;

    /// <summary>
    /// Whether the token at <paramref name="i"/> runs on from a letter, digit or one of <c>:/.</c>
    /// before it, as in W30, P:43 or discord.gg/7F. Kana and kanji don't count: 本日23時 is a time.
    /// </summary>
    private static bool JoinedToPrevious(List<Token> tokens, int i)
    {
        if (i == 0 || !Adjacent(tokens[i - 1], tokens[i]))
            return false;
        var prev = tokens[i - 1];
        return prev.Kind is Kind.Word or Kind.Number || (prev.Kind == Kind.Symbol && prev.Text[0] is ':' or '/' or '.');
    }
}
