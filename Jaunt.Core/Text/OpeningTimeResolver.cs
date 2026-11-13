namespace Jaunt.Core.Text;

/// <summary>A Party Finder region: the data centres one listing can be seen from.</summary>
public enum PartyFinderRegion
{
    /// <summary>North America.</summary>
    NorthAmerica,

    /// <summary>Europe.</summary>
    Europe,

    /// <summary>Japan.</summary>
    Japan,

    /// <summary>Oceania.</summary>
    Oceania,
}

/// <summary>An opening as instants: when it starts and ends, in UTC.</summary>
/// <param name="Now">The ad says it is open now.</param>
/// <param name="Always">The ad says it is always open.</param>
/// <param name="Day">The day it is on, in the ad's time zone, when the ad gives a day or a start time.</param>
/// <param name="Start">When it opens, or null when the ad gives no start time.</param>
/// <param name="End">When it closes, or null when the ad gives no closing time.</param>
public sealed record OpeningWindow(bool Now, bool Always, DateOnly? Day, DateTimeOffset? Start, DateTimeOffset? End)
{
    /// <summary>The same window with its times shown in <paramref name="zone"/>, such as the user's local zone.</summary>
    public OpeningWindow In(TimeZoneInfo zone) => this with
    {
        Start = Start is { } s ? TimeZoneInfo.ConvertTime(s, zone) : null,
        End = End is { } e ? TimeZoneInfo.ConvertTime(e, zone) : null,
    };
}

/// <summary>
/// Turns an <see cref="OpeningTime"/> as written into a <see cref="OpeningWindow"/>: picks the time
/// zone, the day and the UTC offset in force on that day.
/// </summary>
/// <remarks>
/// A written US or Australian zone names the place, not the offset: ads write "EST" all year, so
/// "EST" in July means New York's daylight time. ST (server time) is UTC. With no zone, the region
/// default from <c>aca1be81</c> applies.
/// </remarks>
public static class OpeningTimeResolver
{
    private static readonly Dictionary<string, string> ZoneIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ST"] = "UTC", ["UTC"] = "UTC", ["GMT"] = "UTC",
        ["BST"] = "Europe/London",
        ["CET"] = "Europe/Berlin", ["CEST"] = "Europe/Berlin",
        ["ET"] = "America/New_York", ["EST"] = "America/New_York", ["EDT"] = "America/New_York",
        ["CT"] = "America/Chicago", ["CST"] = "America/Chicago", ["CDT"] = "America/Chicago",
        ["MT"] = "America/Denver", ["MST"] = "America/Denver", ["MDT"] = "America/Denver",
        ["PT"] = "America/Los_Angeles", ["PST"] = "America/Los_Angeles", ["PDT"] = "America/Los_Angeles",
        ["AET"] = "Australia/Sydney", ["AEST"] = "Australia/Sydney", ["AEDT"] = "Australia/Sydney",
        ["ACST"] = "Australia/Adelaide", ["ACDT"] = "Australia/Adelaide",
        ["AWST"] = "Australia/Perth",
        ["NZST"] = "Pacific/Auckland", ["NZDT"] = "Pacific/Auckland",
        ["JST"] = "Asia/Tokyo",
    };

    // Windows IDs, for a runtime that can't map IANA IDs (no ICU).
    private static readonly Dictionary<string, string> WindowsIds = new(StringComparer.Ordinal)
    {
        ["UTC"] = "UTC",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
        ["Australia/Adelaide"] = "Cen. Australia Standard Time",
        ["Australia/Perth"] = "W. Australia Standard Time",
        ["Pacific/Auckland"] = "New Zealand Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
    };

    /// <summary>
    /// The zone an ad's times are in: the one it writes, else the region default. In Europe the
    /// default follows the ad's language: German or French means Berlin, anything else London.
    /// </summary>
    /// <param name="written">The zone as written (<see cref="OpeningTime.Zone"/>), or null.</param>
    /// <param name="region">The region the listing was seen in.</param>
    /// <param name="language">The ad's language as an ISO 639-1 code ("de", "fr", "en"), or null if unknown.</param>
    public static TimeZoneInfo Zone(string? written, PartyFinderRegion region, string? language)
    {
        if (written is not null && ZoneIds.TryGetValue(written, out var id))
            return Find(id);
        return Find(region switch
        {
            PartyFinderRegion.NorthAmerica => "America/New_York",
            PartyFinderRegion.Japan => "Asia/Tokyo",
            PartyFinderRegion.Oceania => "Australia/Sydney",
            _ => language is "de" or "fr" ? "Europe/Berlin" : "Europe/London",
        });
    }

    private static TimeZoneInfo Find(string ianaId)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var zone))
            return zone;
        return TimeZoneInfo.FindSystemTimeZoneById(WindowsId(ianaId));
    }

    /// <summary>The Windows ID for an IANA ID this class uses.</summary>
    internal static string WindowsId(string ianaId) => WindowsIds[ianaId];

    /// <summary>Places an opening on the calendar.</summary>
    /// <param name="time">The opening as the ad writes it.</param>
    /// <param name="seen">When the ad was seen; "today", weekdays and undated times count from here.</param>
    /// <param name="zone">The zone its times are in, from <see cref="Zone"/>.</param>
    /// <remarks>
    /// An undated start is today's in <paramref name="zone"/>, unless yesterday's opening is still
    /// running (9PM-2AM seen at 1AM). A weekday is the next one, counting today. A closing time on
    /// its own is the next one after <paramref name="seen"/>; with a day, one before 06:00 falls after
    /// that day's midnight ("tonight until 2am"). An end at or before the start is the next day.
    /// </remarks>
    public static OpeningWindow Resolve(OpeningTime time, DateTimeOffset seen, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(seen, zone);
        var today = DateOnly.FromDateTime(local.DateTime);
        DateOnly? day = time.Day switch
        {
            OpeningDay.Today => today,
            OpeningDay.Tomorrow => today.AddDays(1),
            OpeningDay.Weekday => today.AddDays(((int)time.Weekday!.Value - (int)today.DayOfWeek + 7) % 7),
            OpeningDay.Date => time.Date,
            _ => null,
        };

        if (time.Start is { } start)
        {
            var date = day ?? today;
            // Yesterday's start is always before seen, so only its end needs checking.
            if (day is null && time.End is { } e && e <= start && seen < Instant(today, e, zone))
            {
                date = today.AddDays(-1); // yesterday's overnight opening is still running
            }

            var opens = Instant(date, start, zone);
            DateTimeOffset? closes = time.End is { } end
                ? Instant(end <= start ? date.AddDays(1) : date, end, zone)
                : null;
            return new OpeningWindow(time.Now, time.Always, date, opens, closes);
        }

        if (time.End is { } closing)
        {
            DateTimeOffset closes;
            if (day is { } d)
            {
                closes = Instant(closing < new TimeOnly(6, 0) ? d.AddDays(1) : d, closing, zone);
            }
            else
            {
                closes = Instant(today, closing, zone);
                if (closes <= seen)
                    closes = Instant(today.AddDays(1), closing, zone);
            }

            return new OpeningWindow(time.Now, time.Always, day, null, closes);
        }

        // A day with no time ("tonight"), or now or always on their own.
        return new OpeningWindow(time.Now, time.Always, day, null, null);
    }

    /// <summary>
    /// A wall-clock time in <paramref name="zone"/> as an instant. A time skipped by the spring
    /// change reads as the hour after it; one repeated in autumn reads as the first.
    /// </summary>
    private static DateTimeOffset Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall))
            wall = wall.AddHours(1);
        var offset = zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).Max() : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset).ToUniversalTime();
    }
}
