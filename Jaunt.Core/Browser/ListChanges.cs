namespace Jaunt.Core.Browser;

/// <summary>
/// When the browser's list, worked out at one time, next needs working out again with no change to
/// the cache: when an opening it could list starts or ends, comes within
/// <see cref="Schedules.OneOffHorizon"/>, or gets close enough that its status line drops the date
/// (<see cref="StatusText.UntilDateAfter"/>), and at local midnight, when the day groups move on.
/// </summary>
public static class ListChanges
{
    /// <summary>
    /// The first time after <paramref name="now"/> at which <see cref="Schedules.Listed"/>, the merge
    /// in <see cref="Duplicates"/>, <see cref="DayGroups.Group"/> or <see cref="StatusText"/> may read
    /// the schedule differently, or null if none will.
    /// </summary>
    public static DateTimeOffset? Next(ListingSchedule schedule, DateTimeOffset now)
    {
        DateTimeOffset? next = null;
        foreach (var occurrence in schedule.Occurrences)
        {
            next = Earliest(next, occurrence.Start, now);
            next = Earliest(next, occurrence.End, now);
            // "Until" drops the date once the closing time is strictly less than a day away, which
            // matters only while open, so not before the start (counted already)
            var dropsDate = occurrence.End - StatusText.UntilDateAfter + TimeSpan.FromTicks(1);
            next = Earliest(next, occurrence.Start is { } start ? Later(dropsDate, start) : dropsDate, now);
            if (schedule.OneOff)
                next = Earliest(next, occurrence.Start - Schedules.OneOffHorizon, now);
        }

        return next;
    }

    /// <summary>The next local midnight after <paramref name="now"/> in <paramref name="zone"/>.</summary>
    public static DateTimeOffset NextDay(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var midnight = local.Date.AddDays(1);
        // A midnight skipped by a daylight saving change is the first moment of the new day
        while (zone.IsInvalidTime(midnight))
            midnight = midnight.AddMinutes(1);
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }

    /// <summary>The earlier of <paramref name="next"/> and <paramref name="at"/>, counting <paramref name="at"/> only when after <paramref name="now"/>.</summary>
    /// <remarks>Comparisons only: the result may be in UTC rather than the offset it was given in.</remarks>
    public static DateTimeOffset? Earliest(DateTimeOffset? next, DateTimeOffset? at, DateTimeOffset now)
    {
        if (at is not { } time || time <= now)
            return next;
        return next is { } soonest ? new DateTimeOffset(Math.Min(time.UtcTicks, soonest.UtcTicks), TimeSpan.Zero) : time;
    }

    // The later of the two, or null with no first; by ticks so a tie needs no comparison to settle
    private static DateTimeOffset? Later(DateTimeOffset? at, DateTimeOffset floor) =>
        at is { } time ? new DateTimeOffset(Math.Max(time.UtcTicks, floor.UtcTicks), TimeSpan.Zero) : null;
}
