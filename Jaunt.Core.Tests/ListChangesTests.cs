using System.Globalization;
using Jaunt.Core.Browser;
using Jaunt.Core.Display;

namespace Jaunt.Core.Tests;

public class ListChangesTests
{
    // Saturday
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo MinusFive = TimeZoneInfo.CreateCustomTimeZone("Test-5", TimeSpan.FromHours(-5), "Test-5", "Test-5");

    private static Occurrence At(double startHours, double endHours) => new(Now.AddHours(startHours), Now.AddHours(endHours));

    private static ListingSchedule Recurring(params Occurrence[] occurrences) => new(false, false, occurrences);

    private static ListingSchedule OneOff(params Occurrence[] occurrences) => new(false, true, occurrences);

    // Everything the browser shows of a schedule at a time: the openings listed and each one's status line
    private static string Shown(ListingSchedule schedule, DateTimeOffset at) =>
        string.Join(" | ", Schedules.Listed(schedule, at).Select(o =>
            $"{o.Start}–{o.End}: {StatusText.Row(o, schedule.AlwaysOpen, at, MinusFive, TimeFormat.TwentyFourHour, DateFormat.DayMonth, CultureInfo.InvariantCulture)}"));

    [Fact]
    public void ARecurringListingOpenNowChangesWhenItCloses() =>
        Assert.Equal(Now.AddHours(2), ListChanges.Next(Recurring(At(-1, 2), At(166, 170)), Now));

    [Fact]
    public void ARecurringListingNotOpenChangesWhenItsNextOpeningStarts() =>
        Assert.Equal(Now.AddHours(3), ListChanges.Next(Recurring(At(-30, -27), At(3, 6), At(171, 174)), Now));

    [Fact]
    public void AnOpeningClosingDaysAheadChangesWhenItsStatusDropsTheDate()
    {
        var schedule = Recurring(new Occurrence(Now.AddHours(-1), Now.AddDays(3)));

        var next = ListChanges.Next(schedule, Now);

        Assert.Equal(Now.AddDays(2).AddTicks(1), next);
        Assert.NotEqual(Shown(schedule, next!.Value.AddTicks(-1)), Shown(schedule, next.Value));
    }

    [Fact]
    public void AOneOffBeyondTheHorizonChangesWhenItComesWithinIt()
    {
        var start = Now.AddDays(40);
        var schedule = OneOff(new Occurrence(start, start.AddHours(3)));

        var next = ListChanges.Next(schedule, Now);

        Assert.Equal(start - Schedules.OneOffHorizon, next);
        Assert.Empty(Schedules.Listed(schedule, next!.Value.AddTicks(-1)));
        Assert.Single(Schedules.Listed(schedule, next.Value));
    }

    [Fact]
    public void ARecurringListingIgnoresTheOneOffHorizon() =>
        Assert.Equal(Now.AddDays(40), ListChanges.Next(Recurring(At(24 * 40, (24 * 40) + 3)), Now));

    [Fact]
    public void NothingChangesForAListingAlwaysOpenOrWithEveryOpeningOver()
    {
        Assert.Null(ListChanges.Next(ListingSchedule.Always, Now));
        Assert.Null(ListChanges.Next(OneOff(At(-5, -2)), Now));
        Assert.Null(ListChanges.Next(OneOff(new Occurrence(null, null)), Now));
    }

    [Fact]
    public void AnOpeningUnderWayWithNoStartChangesWhenItEnds() =>
        Assert.Equal(Now.AddMinutes(30), ListChanges.Next(OneOff(new Occurrence(null, Now.AddMinutes(30))), Now));

    // The rebuild relies on the browser showing the same thing until the time given: checked at every
    // quarter hour up to it, then at it
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhatIsShownStaysTheSameUntilTheNextChangeAndChangesThen(bool oneOff)
    {
        Occurrence[] occurrences = [At(-1, 1.5), At(2, 4), new(Now.AddHours(5), null), At(30, 80), At(24 * 31, (24 * 31) + 2)];
        var schedule = new ListingSchedule(false, oneOff, occurrences);
        var at = Now;
        var changes = 0;
        while (ListChanges.Next(schedule, at) is { } next)
        {
            var before = Shown(schedule, at);
            for (var t = at; t < next; t = t.AddMinutes(15))
                Assert.Equal(before, Shown(schedule, t));
            Assert.Equal(before, Shown(schedule, next.AddTicks(-1)));
            at = next;
            changes++;
        }

        Assert.True(changes >= 5);
        Assert.Equal(Shown(schedule, at), Shown(schedule, at.AddDays(60)));
    }

    [Fact]
    public void TheNextDayStartsAtLocalMidnight() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromHours(-5)), ListChanges.NextDay(Now, MinusFive));

    [Fact]
    public void AtMidnightTheNextDayIsTheOneAfter()
    {
        var midnight = new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.FromHours(-5));

        Assert.Equal(midnight.AddDays(1), ListChanges.NextDay(midnight, MinusFive));
    }

    [Fact]
    public void AMidnightSkippedByDaylightSavingIsTheNewDaysFirstMoment()
    {
        // Chile moves its clocks from 00:00 to 01:00 on the first Sunday of September
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        var saturday = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.FromHours(-4));
        Assert.True(santiago.IsInvalidTime(new DateTime(2026, 9, 6, 0, 0, 0)));

        Assert.Equal(new DateTimeOffset(2026, 9, 6, 1, 0, 0, TimeSpan.FromHours(-3)), ListChanges.NextDay(saturday, santiago));
    }

    [Fact]
    public void EarliestCountsOnlyTimesAfterNow()
    {
        Assert.Equal(Now.AddHours(1), ListChanges.Earliest(Now.AddHours(2), Now.AddHours(1), Now));
        Assert.Equal(Now.AddHours(2), ListChanges.Earliest(Now.AddHours(2), Now.AddHours(3), Now));
        Assert.Equal(Now.AddHours(2), ListChanges.Earliest(Now.AddHours(2), Now, Now));
        Assert.Equal(Now.AddHours(2), ListChanges.Earliest(Now.AddHours(2), null, Now));
        Assert.Equal(Now.AddHours(1), ListChanges.Earliest(null, Now.AddHours(1), Now));
        Assert.Null(ListChanges.Earliest(null, Now.AddHours(-1), Now));
    }
}
