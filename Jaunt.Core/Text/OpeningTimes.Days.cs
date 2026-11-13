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
            var next = At(tokens, i + 1);
            var afterNext = At(tokens, i + 2);
            if (tokens[i] is { Kind: Kind.Number, Text: "24" } && IsSymbol(next, '/') && Adjacent(tokens[i], next.Value)
                && afterNext is { Kind: Kind.Number, Text: "7" } && Adjacent(next.Value, afterNext.Value))
            {
                used[i] = used[i + 1] = used[i + 2] = true;
                found = true;
            }
            else if (IsWord(tokens[i], "always") && IsWord(next, "open"))
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

            var numbered = t.Kind == Kind.Number ? NumberedDateAt(tokens, i, seen) : null;
            if (numbered is not null)
                return numbered;

            if (t.Kind == Kind.Number && t.Text.Length <= 2)
            {
                // 16th Nov
                var j = i + 1;
                var suffix = At(tokens, j);
                if (suffix is { Kind: Kind.Word } && Adjacent(t, suffix.Value) && suffix.Value.Text.ToLowerInvariant() is "st" or "nd" or "rd" or "th")
                    j++;
                var month = MonthOf(At(tokens, j));
                var named = month is not null ? Day(month.Value, t, seen) : null;
                if (named is not null)
                    return named;
            }

            var mo = MonthOf(t);
            if (mo is not null)
            {
                // Nov 5th, Nov. 5
                var j = i + 1;
                if (IsSymbol(At(tokens, j), '.'))
                    j++;
                var n = At(tokens, j);
                var monthFirst = n is { Kind: Kind.Number } && n.Value.Text.Length <= 2 ? Day(mo.Value, n.Value, seen) : null;
                if (monthFirst is not null)
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
        var sep = At(tokens, i + 1);
        var b = At(tokens, i + 2);
        if (JoinedToPrevious(tokens, i) || !(IsSymbol(sep, '/') || IsSymbol(sep, '.')) || !Adjacent(a, sep.Value)
            || b is not { Kind: Kind.Number } || !Adjacent(sep.Value, b.Value))
        {
            return null;
        }

        var second = b.Value;
        var trailingDot = false;
        var after = At(tokens, i + 3);
        if (after is not null && Adjacent(second, after.Value))
        {
            if (after.Value.Kind != Kind.Symbol || IsSymbol(after, '/') || IsSymbol(after, ':'))
                return null;
            if (IsSymbol(after, '.'))
            {
                var more = At(tokens, i + 4);
                if (more is { Kind: Kind.Number } && Adjacent(after.Value, more.Value))
                    return null;
                trailingDot = true;
            }
        }

        if (IsSymbol(sep, '.') && second.Text.Length != 2 && !trailingDot)
            return null;
        if (IsSymbol(sep, '.') && !trailingDot && after is { Kind: Kind.Word } && after.Value.Start - second.End <= 1
            && after.Value.Text.ToUpperInvariant() is "AM" or "PM")
        {
            return null; // 10.30 pm is a time
        }

        return NumberedDate(int.Parse(a.Text, CultureInfo.InvariantCulture), int.Parse(second.Text, CultureInfo.InvariantCulture), seen);
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

    private static int? MonthOf(Token? t) => t is { Kind: Kind.Word } ? Month(t.Value.Text) : null;

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
                else if (JapaneseWeekday(rest) >= 0)
                {
                    weekday = (DayOfWeek)JapaneseWeekday(rest);
                    after = i + 2;
                    if (At(tokens, after) is { Kind: Kind.Cjk, Text: "日" })
                        after++;
                }
            }

            if (weekday is null)
                continue;

            // Skip up to two of . , @ - between the weekday and its time.
            for (var skipped = 0; skipped < 2 && IsDaySeparator(At(tokens, after)); skipped++)
                after++;
            if (spanAt.TryGetValue(after, out var span))
                weekdays.TryAdd(span, weekday.Value);
        }

        return (day, weekdays);
    }

    /// <summary>The weekday of 金曜 and the like at the start of <paramref name="rest"/>, in DayOfWeek order, or -1.</summary>
    private static int JapaneseWeekday(ReadOnlySpan<char> rest) =>
        rest is [_, '曜', ..] ? JapaneseWeekdays.IndexOf(rest[0], StringComparison.Ordinal) : -1;

    private static bool IsDaySeparator(Token? t) => IsSymbol(t, '.') || IsSymbol(t, ',') || IsSymbol(t, '@') || IsSymbol(t, '-');
}
