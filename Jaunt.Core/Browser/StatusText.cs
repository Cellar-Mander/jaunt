using System.Globalization;
using Jaunt.Core.Display;

namespace Jaunt.Core.Browser;

/// <summary>A listed opening's status line, in local time and the Settings formats.</summary>
public static class StatusText
{
    /// <summary>
    /// For a list row, under a day divider that already gives the date: "Open now · 24h", "Open now",
    /// "Open now · until 23:00" (with the date when it closes after the next 24 hours), or the times,
    /// "20:00–23:00", or "20:00" with no closing time. "Closed" once it has ended, as it can before the
    /// list is next worked out.
    /// </summary>
    public static string Row(Occurrence when, bool alwaysOpen, DateTimeOffset now, TimeZoneInfo zone, TimeFormat timeFormat, DateFormat dateFormat, CultureInfo locale)
    {
        if (alwaysOpen)
            return "Open now · 24h";
        if (when.IsOpen(now))
            return OpenNow(when, now, zone, timeFormat, dateFormat, locale);
        if (when.End <= now)
            return "Closed";

        var start = Local(when.Start!.Value, zone);
        var from = DisplayFormats.FormatTime(start, timeFormat, locale);
        return when.End is { } end ? $"{from}–{DisplayFormats.FormatTime(Local(end, zone), timeFormat, locale)}" : from;
    }

    /// <summary>
    /// For the detail pane, which has no divider: as <see cref="Row"/> while open, else the date and
    /// times, "Sat 21 Nov 20:00–23:00", or "Sat 21 Nov 20:00" with no closing time.
    /// </summary>
    public static string Detail(Occurrence when, bool alwaysOpen, DateTimeOffset now, TimeZoneInfo zone, TimeFormat timeFormat, DateFormat dateFormat, CultureInfo locale)
    {
        if (alwaysOpen || when.IsOpen(now) || when.End <= now)
            return Row(when, alwaysOpen, now, zone, timeFormat, dateFormat, locale);

        var start = Local(when.Start!.Value, zone);
        return when.End is { } end
            ? DisplayFormats.FormatSpan(start, Local(end, zone), timeFormat, dateFormat, locale)
            : $"{DisplayFormats.FormatDate(start, dateFormat, locale)} {DisplayFormats.FormatTime(start, timeFormat, locale)}";
    }

    private static string OpenNow(Occurrence when, DateTimeOffset now, TimeZoneInfo zone, TimeFormat timeFormat, DateFormat dateFormat, CultureInfo locale)
    {
        if (when.End is not { } end)
            return "Open now";

        var local = Local(end, zone);
        var time = DisplayFormats.FormatTime(local, timeFormat, locale);
        return end - now < TimeSpan.FromDays(1)
            ? $"Open now · until {time}"
            : $"Open now · until {DisplayFormats.FormatDate(local, dateFormat, locale)} {time}";
    }

    private static DateTime Local(DateTimeOffset instant, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(instant, zone).DateTime;
}
