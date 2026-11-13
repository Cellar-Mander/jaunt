namespace Jaunt.Core.Text;

/// <summary>Which day an <see cref="OpeningTime"/> is on, as the ad states it.</summary>
public enum OpeningDay
{
    /// <summary>The ad names no day.</summary>
    None,

    /// <summary>"today", "tonight", 本日.</summary>
    Today,

    /// <summary>"tomorrow", 明日.</summary>
    Tomorrow,

    /// <summary>A day of the week, in <see cref="OpeningTime.Weekday"/>.</summary>
    Weekday,

    /// <summary>A calendar date, in <see cref="OpeningTime.Date"/>.</summary>
    Date,
}

/// <summary>One opening an ad states, as written: times are wall-clock times in <see cref="Zone"/>.</summary>
/// <param name="Now">The ad says it is open now.</param>
/// <param name="Always">The ad says it is always open (24/7).</param>
/// <param name="Day">Which day, if the ad says.</param>
/// <param name="Weekday">The weekday when <see cref="Day"/> is <see cref="OpeningDay.Weekday"/>.</param>
/// <param name="Date">The date when <see cref="Day"/> is <see cref="OpeningDay.Date"/>.</param>
/// <param name="Start">When it opens, on the 24-hour clock.</param>
/// <param name="End">When it closes, on the 24-hour clock; earlier than <see cref="Start"/> means after midnight.</param>
/// <param name="Zone">The time zone as written, upper-cased (<c>ST</c>, <c>EST</c>, <c>AEDT</c>), or null.</param>
public sealed record OpeningTime(
    bool Now,
    bool Always,
    OpeningDay Day,
    DayOfWeek? Weekday,
    DateOnly? Date,
    TimeOnly? Start,
    TimeOnly? End,
    string? Zone);
