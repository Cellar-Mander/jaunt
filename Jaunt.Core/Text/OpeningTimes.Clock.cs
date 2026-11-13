using System.Globalization;

namespace Jaunt.Core.Text;

public static partial class OpeningTimes
{
    private readonly record struct Clock(int Hour, int? Minute, char? Meridiem, bool LeadingZero);

    private sealed record Span(Clock? Start, Clock? End, string? Zone, int First, int Last);

    private static readonly string[] Zones =
    [
        "ST", "UTC", "GMT", "BST", "CET", "CEST",
        "ET", "EST", "EDT", "CT", "CST", "CDT", "MT", "MST", "MDT", "PT", "PST", "PDT",
        "AET", "AEST", "AEDT", "ACST", "ACDT", "AWST", "NZST", "NZDT", "JST",
    ];

    private static List<Span> FindSpans(string text, List<Token> tokens, bool[] used)
    {
        var spans = new List<Span>();
        var i = 0;
        while (i < tokens.Count)
        {
            var first = !used[i] && tokens[i].Kind == Kind.Number && !JoinedToPrevious(tokens, i) ? ReadClock(tokens, i) : null;
            if (first is null)
            {
                i++;
                continue;
            }

            var (c1, z1, ji1, next1) = first.Value;

            // A range: a separator, then a second time.
            if (IsRangeSeparator(At(tokens, next1)))
            {
                var j = next1 + 1;
                while (At(tokens, j) is { } more && IsSymbol(more, '-'))
                    j++;
                var second = At(tokens, j) is { Kind: Kind.Number } && !used[j] ? ReadClock(tokens, j) : null;
                if (second is not null && (Qualified(c1, z1, ji1) || Qualified(second.Value.Clock, second.Value.Zone, second.Value.Ji)))
                {
                    spans.Add(new Span(c1, second.Value.Clock, second.Value.Zone ?? z1, i, second.Value.Next - 1));
                    i = second.Value.Next;
                    continue;
                }
            }

            var endOnly = EndPrefix(tokens, i) || EndSuffix(text, tokens, next1);
            if (endOnly || Qualified(c1, z1, ji1))
            {
                spans.Add(endOnly ? new Span(null, c1, z1, i, next1 - 1) : new Span(c1, null, z1, i, next1 - 1));
                i = next1;
                continue;
            }

            i++;
        }

        return spans;
    }

    /// <summary>A dash, en or em dash, tilde, "to", "till", "til" or "until" between two times.</summary>
    private static bool IsRangeSeparator(Token? t) =>
        t is { Kind: Kind.Symbol, Text: "-" or "–" or "—" or "~" }
        || (t is { Kind: Kind.Word } && t.Value.Text.ToLowerInvariant() is "to" or "till" or "til" or "until");

    /// <summary>
    /// Whether a lone number is plainly a time: AM/PM, a zone, minutes or 時. "@" alone isn't
    /// enough, since ads also write party sizes that way ("@2").
    /// </summary>
    private static bool Qualified(Clock c, string? zone, bool ji) =>
        c.Meridiem is not null || zone is not null || c.Minute is not null || ji;

    /// <summary>"until 11pm", "till", "--2am", "~17:00", "〆14:00".</summary>
    private static bool EndPrefix(List<Token> tokens, int i)
    {
        if (At(tokens, i - 1) is not { } prev)
            return false;
        if (IsWord(prev, "until") || IsWord(prev, "till") || IsWord(prev, "til"))
            return true;
        if ((IsSymbol(prev, '~') || prev is { Kind: Kind.Cjk, Text: "〆" }) && Adjacent(prev, tokens[i]))
            return true;
        return IsSymbol(prev, '-') && At(tokens, i - 2) is { } prev2 && IsSymbol(prev2, '-') && Adjacent(prev2, prev);
    }

    /// <summary>"18時〆", "16時close", "23時くらいまで", "~17:00〆".</summary>
    private static bool EndSuffix(string text, List<Token> tokens, int next)
    {
        if (At(tokens, next) is not { } t)
            return false;
        var rest = text.AsSpan(t.Start);
        return rest.StartsWith("〆", StringComparison.Ordinal) || rest.StartsWith("まで", StringComparison.Ordinal)
            || rest.StartsWith("くらいまで", StringComparison.Ordinal) || rest.StartsWith("close", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads H, H:MM or H.MM (the last only with AM/PM), then AM/PM and a zone, which may be joined
    /// ("10pm-1amEST", "6MST") or spaced ("11 pm", "7 PM ST"), or 時.
    /// </summary>
    private static (Clock Clock, string? Zone, bool Ji, int Next)? ReadClock(List<Token> tokens, int i)
    {
        var h = tokens[i];
        if (h.Text.Length > 2)
            return null;
        var hour = int.Parse(h.Text, CultureInfo.InvariantCulture);
        int? minute = null;
        var dotted = false;
        var next = i + 1;

        if (At(tokens, i + 1) is { } sep && (IsSymbol(sep, ':') || IsSymbol(sep, '.')) && Adjacent(h, sep)
            && At(tokens, i + 2) is { Kind: Kind.Number, Text.Length: 2 } m && Adjacent(sep, m))
        {
            var value = int.Parse(m.Text, CultureInfo.InvariantCulture);
            if (value <= 59)
            {
                minute = value;
                dotted = sep.Text[0] == '.';
                next = i + 3;
            }
        }

        if (At(tokens, next) is { Kind: Kind.Cjk, Text: "時" } ji && Adjacent(tokens[next - 1], ji))
        {
            if (hour > 30 || minute is not null)
                return null;
            // Stryker disable once Boolean : 時 makes the ad Japanese text, read on the 24-hour clock whatever LeadingZero says
            var clock = new Clock(hour, null, null, false);
            return (clock, null, true, next + 1);
        }

        char? meridiem = null;
        string? zone = null;
        if (At(tokens, next) is { Kind: Kind.Word } w && w.Start - tokens[next - 1].End <= 1)
        {
            var joined = Adjacent(tokens[next - 1], w);
            var upper = w.Text.ToUpperInvariant();
            if (joined && minute is null && w.Text == "st" && hour is 1 or 21)
                return null; // 1st, 21st: an ordinal, while server time is written ST (20ST, the glyph)
            if (Zones.Contains(upper))
            {
                zone = upper;
                next++;
            }
            else if (upper is "AM" or "PM" || (joined && upper is "A" or "P"))
            {
                meridiem = upper[0];
                next++;
                if (At(tokens, next) is { Kind: Kind.Word } z && z.Start - w.End <= 1 && Zones.Contains(z.Text.ToUpperInvariant()))
                {
                    zone = z.Text.ToUpperInvariant();
                    next++;
                }
            }
            else if (joined && SplitMeridiem(upper) is var (mer, z2))
            {
                meridiem = mer;
                zone = z2;
                next++;
            }
        }

        if (dotted && meridiem is null)
        {
            minute = null;
            next = i + 1;
            zone = null;
        }

        var limit = meridiem is null ? 24 : 12;
        if (hour > limit)
            return null;
        return (new Clock(hour, minute, meridiem, h.Text.Length == 2 && h.Text[0] == '0'), zone, false, next);
    }

    /// <summary>"AMEST" into A and EST, "PMET" into P and ET.</summary>
    private static (char Meridiem, string Zone)? SplitMeridiem(string upper)
    {
        foreach (var prefix in (string[])["AM", "PM", "A", "P"])
        {
            if (upper.StartsWith(prefix, StringComparison.Ordinal) && Zones.Contains(upper[prefix.Length..]))
                return (prefix[0], upper[prefix.Length..]);
        }

        return null;
    }

    /// <summary>A start and optional end on the 24-hour clock, filling in AM/PM where only one side says.</summary>
    private static (TimeOnly Start, TimeOnly? End) To24(Clock start, Clock? end, bool twentyFourHour)
    {
        if (end is not { } e)
            return (Single(start, twentyFourHour), null);

        if (start.Meridiem is null && e.Meridiem is not null && !Literal(start, twentyFourHour))
        {
            var endTime = Single(e, twentyFourHour);
            return (Before(start, endTime), endTime);
        }

        var startTime = Single(start, twentyFourHour);
        return (startTime, e.Meridiem is null && !Literal(e, twentyFourHour) ? After(e, startTime) : Single(e, twentyFourHour));
    }

    /// <summary>Whether a clock without AM/PM is already on the 24-hour clock.</summary>
    private static bool Literal(Clock c, bool twentyFourHour) => twentyFourHour || c.Hour > 12 || c.Hour == 0 || c.LeadingZero;

    /// <summary>One time on its own: AM/PM as written, otherwise 1 to 11 read as evening.</summary>
    private static TimeOnly Single(Clock c, bool twentyFourHour)
    {
        var minute = c.Minute ?? 0;
        if (c.Meridiem is { } m)
            return new TimeOnly((c.Hour % 12) + (m == 'P' ? 12 : 0), minute);
        if (Literal(c, twentyFourHour))
            return new TimeOnly(c.Hour % 24, minute);
        return new TimeOnly(c.Hour == 12 ? 12 : c.Hour + 12, minute);
    }

    /// <summary>The reading of a bare hour that falls within 12 hours before <paramref name="end"/>.</summary>
    private static TimeOnly Before(Clock c, TimeOnly end)
    {
        var am = new TimeOnly(c.Hour % 12, c.Minute ?? 0);
        var pm = am.AddHours(12);
        // Stryker disable once Equality : the readings are 12 hours apart, so their distances to end never tie
        return Forward(am, end) <= Forward(pm, end) ? am : pm;
    }

    /// <summary>The reading of a bare hour that falls within 12 hours after <paramref name="start"/>.</summary>
    private static TimeOnly After(Clock c, TimeOnly start)
    {
        var am = new TimeOnly(c.Hour % 12, c.Minute ?? 0);
        var pm = am.AddHours(12);
        // Stryker disable once Equality : the readings are 12 hours apart, so their distances from start never tie
        return Forward(start, am) <= Forward(start, pm) ? am : pm;
    }

    /// <summary>How far forward from <paramref name="from"/> to <paramref name="to"/>, wrapping at midnight; equal times are a whole day apart.</summary>
    private static TimeSpan Forward(TimeOnly from, TimeOnly to) => to == from ? TimeSpan.FromDays(1) : to - from;
}
