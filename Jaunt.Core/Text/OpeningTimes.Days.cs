using System.Globalization;

namespace Jaunt.Core.Text;

public static partial class OpeningTimes
{
    // "tues", "thurs" and plurals such as "Mondays" are read by dropping a final s.
    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mon"] = DayOfWeek.Monday, ["monday"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday, ["tuesday"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday, ["wednesday"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday, ["thur"] = DayOfWeek.Thursday, ["thursday"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday, ["friday"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday, ["saturday"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday, ["sunday"] = DayOfWeek.Sunday,
    };

    private const string JapaneseWeekdays = "日月火水木金土"; // in DayOfWeek order

    private static readonly string[] Months =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>"24/7" or "always open".</summary>
    private static bool FindAlways(List<Token> tokens, bool[] used)
    {
        var found = false;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is { Kind: Kind.Number, Text: "24" } && At(tokens, i + 1) is { } slash && IsSymbol(slash, '/')
                && Adjacent(tokens[i], slash) && At(tokens, i + 2) is { Kind: Kind.Number, Text: "7" } seven && Adjacent(slash, seven))
            {
                used[i] = used[i + 1] = used[i + 2] = true;
                found = true;
            }
            else if (IsWord(tokens[i], "always") && At(tokens, i + 1) is { } open && IsWord(open, "open"))
            {
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// The first date: 7/11, 07.11., Nov 5th, 16th Nov. Skips the tokens of 24/7. Its own tokens
    /// need no marking: none of them can start a time, since a time needs AM/PM, a zone, a colon
    /// or 時 right after its hour, and <see cref="NumberedDateAt"/> refuses a dotted date followed
    /// by AM or PM.
    /// </summary>
    private static DateOnly? FindDate(List<Token> tokens, bool[] used, DateOnly seen)
    {
        for (var i = 0; i < tokens.Count; i++)
        {
            if (used[i])
                continue;
            var t = tokens[i];

            if (t.Kind == Kind.Number && NumberedDateAt(tokens, i, seen) is { } d)
                return d;

            if (t.Kind == Kind.Number && t.Text.Length <= 2)
            {
                // 16th Nov
                var j = i + 1;
                if (At(tokens, j) is { Kind: Kind.Word } suffix && Adjacent(t, suffix) && suffix.Text.ToLowerInvariant() is "st" or "nd" or "rd" or "th")
                    j++;
                if (At(tokens, j) is { Kind: Kind.Word } month && Month(month.Text) is { } m && Day(m, t, seen) is { } named)
                    return named;
            }

            if (t.Kind == Kind.Word && Month(t.Text) is { } mo)
            {
                // Nov 5th, Nov. 5
                var j = i + 1;
                if (At(tokens, j) is { } dot && IsSymbol(dot, '.'))
                    j++;
                if (At(tokens, j) is { Kind: Kind.Number } n && n.Text.Length <= 2 && Day(mo, n, seen) is { } monthFirst)
                    return monthFirst;
            }
        }

        return null;
    }

    /// <summary>
    /// a/b or a.b at <paramref name="i"/>, standing on its own: not part of a run such as W2/30,
    /// 2.3.4.6 or 80s/90s/00. A dotted date needs a two-digit second number or a dot after it
    /// (07.11, 7.11.), so map coordinates like (13.8,11.4) aren't dates.
    /// </summary>
    private static DateOnly? NumberedDateAt(List<Token> tokens, int i, DateOnly seen)
    {
        var a = tokens[i];
        if (JoinedToPrevious(tokens, i) || At(tokens, i + 1) is not { } sep || !Adjacent(a, sep)
            || !(IsSymbol(sep, '/') || IsSymbol(sep, '.')) || At(tokens, i + 2) is not { Kind: Kind.Number } b || !Adjacent(sep, b))
        {
            return null;
        }

        var trailingDot = false;
        if (At(tokens, i + 3) is { } after && Adjacent(b, after))
        {
            if (after.Kind != Kind.Symbol || IsSymbol(after, '/') || IsSymbol(after, ':'))
                return null;
            if (IsSymbol(after, '.'))
            {
                if (At(tokens, i + 4) is { Kind: Kind.Number } more && Adjacent(after, more))
                    return null;
                trailingDot = true;
            }
        }

        if (IsSymbol(sep, '.') && b.Text.Length != 2 && !trailingDot)
            return null;
        if (IsSymbol(sep, '.') && !trailingDot && At(tokens, i + 3) is { Kind: Kind.Word } w && w.Start - b.End <= 1
            && w.Text.ToUpperInvariant() is "AM" or "PM")
        {
            return null; // 10.30 pm is a time
        }

        return NumberedDate(int.Parse(a.Text, CultureInfo.InvariantCulture), int.Parse(b.Text, CultureInfo.InvariantCulture), seen);
    }

    private static int? Month(string word)
    {
        if (word.Length < 3)
            return null;
        var lower = word.ToLowerInvariant();
        var index = Array.IndexOf(Months, lower[..3]);
        if (index < 0)
            return null;
        var full = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(index + 1).ToLowerInvariant();
        return lower.Length == 3 || full == lower || (lower == "sept") ? index + 1 : null;
    }

    private static DateOnly? Day(int month, Token day, DateOnly seen) =>
        Nearest(month, int.Parse(day.Text, CultureInfo.InvariantCulture), seen);

    /// <summary>a/b as day/month or month/day, whichever falls nearer the day the ad was seen.</summary>
    private static DateOnly? NumberedDate(int a, int b, DateOnly seen)
    {
        var monthFirst = Nearest(a, b, seen);
        var dayFirst = Nearest(b, a, seen);
        if (monthFirst is null)
            return dayFirst;
        if (dayFirst is null)
            return monthFirst;
        return Distance(monthFirst.Value, seen) <= Distance(dayFirst.Value, seen) ? monthFirst : dayFirst;
    }

    /// <summary>The date with this month and day nearest to <paramref name="seen"/>, in its year or the ones either side.</summary>
    private static DateOnly? Nearest(int month, int day, DateOnly seen)
    {
        if (month is < 1 or > 12 || day < 1)
            return null;
        DateOnly? best = null;
        for (var year = seen.Year - 1; year <= seen.Year + 1; year++)
        {
            if (day > DateTime.DaysInMonth(year, month))
                continue;
            var candidate = new DateOnly(year, month, day);
            if (best is null || Distance(candidate, seen) < Distance(best.Value, seen))
                best = candidate;
        }

        return best;
    }

    private static int Distance(DateOnly a, DateOnly b) => Math.Abs(a.DayNumber - b.DayNumber);

    /// <summary>
    /// Day words that apply to the whole ad (today, tonight, tomorrow) and weekdays, which apply only
    /// to a time or date right after them: "FRI 17 ST" is Friday, "Goth Friday w DJs" names an event.
    /// </summary>
    private static (OpeningDay Day, Dictionary<Span, DayOfWeek> Weekdays) FindDays(
        string text, List<Token> tokens, List<Span> spans)
    {
        var day = OpeningDay.None;
        var weekdays = new Dictionary<Span, DayOfWeek>();
        var spanAt = spans.ToDictionary(s => s.First);

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            DayOfWeek? weekday = null;
            var after = i + 1;

            if (t.Kind == Kind.Word)
            {
                var lower = t.Text.ToLowerInvariant();
                if (lower is "today" or "tonight" or "tonite")
                    day = day == OpeningDay.None ? OpeningDay.Today : day;
                else if (lower == "tomorrow")
                    day = day == OpeningDay.None ? OpeningDay.Tomorrow : day;
                else if (Weekdays.TryGetValue(lower, out var w) || (lower.EndsWith('s') && Weekdays.TryGetValue(lower[..^1], out w)))
                    weekday = w;
            }
            else if (t.Kind == Kind.Cjk)
            {
                var rest = text.AsSpan(t.Start);
                if (rest.StartsWith("本日", StringComparison.Ordinal) || rest.StartsWith("今日", StringComparison.Ordinal)
                    || rest.StartsWith("今夜", StringComparison.Ordinal))
                {
                    day = day == OpeningDay.None ? OpeningDay.Today : day;
                }
                else if (rest.StartsWith("明日", StringComparison.Ordinal))
                {
                    day = day == OpeningDay.None ? OpeningDay.Tomorrow : day;
                }
                else if (rest is [var first, '曜', ..] && JapaneseWeekdays.IndexOf(first, StringComparison.Ordinal) is var k and >= 0)
                {
                    weekday = (DayOfWeek)k;
                    after = i + 2;
                    if (At(tokens, after) is { Kind: Kind.Cjk, Text: "日" })
                        after++;
                }
            }

            if (weekday is null)
                continue;

            // Skip up to two of . , @ - between the weekday and its time.
            for (var skipped = 0; skipped < 2 && At(tokens, after) is { } s && (IsSymbol(s, '.') || IsSymbol(s, ',') || IsSymbol(s, '@') || IsSymbol(s, '-')); skipped++)
                after++;
            if (spanAt.TryGetValue(after, out var span))
                weekdays.TryAdd(span, weekday.Value);
        }

        return (day, weekdays);
    }
}
