using System.Globalization;

namespace Jaunt.Core.Display;

/// <summary>How times are shown. Saved by number, so only ever append.</summary>
public enum TimeFormat
{
    /// <summary>12- or 24-hour, as the system's locale writes times.</summary>
    System,
    TwelveHour,
    TwentyFourHour,
}

/// <summary>How dates are shown. Saved by number, so only ever append.</summary>
public enum DateFormat
{
    /// <summary>As the system's locale orders year, month and day.</summary>
    System,

    /// <summary>Sat 14 Nov</summary>
    DayMonth,

    /// <summary>Sat Nov 14</summary>
    MonthDay,

    /// <summary>2026-11-14</summary>
    Iso,
}

/// <summary>Formats listing times and dates. Month and day names are English, like the rest of the plugin.</summary>
public static class DisplayFormats
{
    private static readonly CultureInfo English = CultureInfo.InvariantCulture;

    /// <summary>The format <see cref="TimeFormat.System"/> stands for: 12-hour when the locale writes AM/PM.</summary>
    public static TimeFormat Resolve(TimeFormat format, CultureInfo locale) =>
        format != TimeFormat.System ? format
        : locale.DateTimeFormat.ShortTimePattern.Contains('t', StringComparison.Ordinal) ? TimeFormat.TwelveHour
        : TimeFormat.TwentyFourHour;

    /// <summary>
    /// The format <see cref="DateFormat.System"/> stands for: ISO when the locale writes the year
    /// first, month-day when it writes the month before the day, otherwise day-month.
    /// </summary>
    public static DateFormat Resolve(DateFormat format, CultureInfo locale)
    {
        if (format != DateFormat.System)
            return format;

        var pattern = locale.DateTimeFormat.ShortDatePattern;
        if (pattern.StartsWith('y'))
            return DateFormat.Iso;

        var month = pattern.IndexOf('M', StringComparison.Ordinal);
        var day = pattern.IndexOf('d', StringComparison.Ordinal);
        var hasMonth = month >= 0;
        // Stryker disable once Equality : M and d are different characters, so a found month never sits at day's index
        var monthFirst = hasMonth && month < day;
        return monthFirst ? DateFormat.MonthDay : DateFormat.DayMonth;
    }

    public static string FormatTime(DateTime time, TimeFormat format, CultureInfo locale) =>
        Resolve(format, locale) == TimeFormat.TwelveHour
            ? time.ToString("h:mm tt", English)
            : time.ToString("HH:mm", English);

    public static string FormatDate(DateTime date, DateFormat format, CultureInfo locale) =>
        Resolve(format, locale) switch
        {
            DateFormat.MonthDay => date.ToString("ddd MMM d", English),
            DateFormat.Iso => date.ToString("yyyy-MM-dd", English),
            _ => date.ToString("ddd d MMM", English),
        };
}
