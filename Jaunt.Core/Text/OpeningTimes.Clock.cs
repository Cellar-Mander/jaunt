using System.Globalization;

namespace Jaunt.Core.Text;

public static partial class OpeningTimes
{
    private readonly record struct Clock(int Hour, int? Minute, char? Meridiem, bool LeadingZero);

    private sealed record Span(Clock Start, string? Zone);

    private static readonly string[] Zones =
    [
        "ST", "UTC", "GMT", "BST", "CET", "CEST",
        "ET", "EST", "EDT", "CT", "CST", "CDT", "MT", "MST", "MDT", "PT", "PST", "PDT",
        "AET", "AEST", "AEDT", "ACST", "ACDT", "AWST", "NZST", "NZDT", "JST",
    ];

    private static List<Span> FindSpans(List<Token> tokens)
    {
        var spans = new List<Span>();
        var i = 0;
        while (i < tokens.Count)
        {
            var read = tokens[i].Kind == Kind.Number && !JoinedToPrevious(tokens, i) ? ReadClock(tokens, i) : null;
            if (read is null || !Qualified(read.Value.Clock, read.Value.Zone, read.Value.Ji))
            {
                i++;
                continue;
            }

            spans.Add(new Span(read.Value.Clock, read.Value.Zone));
            i = read.Value.Next;
        }

        return spans;
    }

    /// <summary>
    /// Whether a lone number is plainly a time: AM/PM, a zone, minutes or 時. "@" alone isn't
    /// enough, since ads also write party sizes that way ("@2").
    /// </summary>
    private static bool Qualified(Clock c, string? zone, bool ji) =>
        c.Meridiem is not null || zone is not null || c.Minute is not null || ji;

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
}
