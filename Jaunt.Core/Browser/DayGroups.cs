using System.Globalization;
using Jaunt.Core.Display;

namespace Jaunt.Core.Browser;

/// <summary>What a day group's divider says.</summary>
public enum DayGroupKind
{
    OpenNow,
    Today,
    Tomorrow,
    Date,
}

/// <summary>One listed opening of a listing.</summary>
/// <param name="Item">The listing, as the caller describes it.</param>
/// <param name="Title">Sorts entries that open at the same time.</param>
/// <param name="Key">Unique to the listing; orders entries with the same start and title, so the order doesn't change between rebuilds.</param>
/// <param name="AlwaysOpen">A 24/7 venue, listed last in "Open now".</param>
public sealed record BrowserEntry<T>(T Item, string Title, string Key, Occurrence When, bool AlwaysOpen);

/// <summary>A divider and the entries under it.</summary>
/// <param name="Date">The local date the entries open on; for <see cref="DayGroupKind.OpenNow"/>, today.</param>
public sealed record DayGroup<T>(DayGroupKind Kind, DateOnly Date, IReadOnlyList<BrowserEntry<T>> Entries);

/// <summary>
/// Groups listed openings by the local day they start on: "Open now" first, then today's later
/// openings, "Tomorrow", then one group per date. Within a group, earliest start first, then title.
/// "Open now" lists the soonest to close first, taking an opening with a start but no end to close
/// <see cref="AssumedLength"/> after it starts; openings with neither come after, and 24/7 venues last.
/// </summary>
public static class DayGroups
{
    /// <summary>How long "Open now" assumes an opening with no closing time lasts, for sorting only.</summary>
    public static readonly TimeSpan AssumedLength = TimeSpan.FromHours(3);

    public static IReadOnlyList<DayGroup<T>> Group<T>(IEnumerable<BrowserEntry<T>> entries, DateTimeOffset now, TimeZoneInfo zone)
    {
        var today = LocalDate(now, zone);
        var openNow = new List<BrowserEntry<T>>();
        var byDate = new SortedDictionary<DateOnly, List<BrowserEntry<T>>>();
        foreach (var entry in entries)
        {
            if (entry.AlwaysOpen || entry.When.IsOpen(now))
            {
                openNow.Add(entry);
                continue;
            }

            // Not open, so it has a start after now: an occurrence without a start is open until it ends
            var date = LocalDate(entry.When.Start!.Value, zone);
            if (!byDate.TryGetValue(date, out var list))
                byDate[date] = list = [];
            list.Add(entry);
        }

        var groups = new List<DayGroup<T>>(byDate.Count + 1);
        if (openNow.Count > 0)
        {
            openNow.Sort(CompareOpenNow);
            groups.Add(new DayGroup<T>(DayGroupKind.OpenNow, today, openNow));
        }

        foreach (var (date, list) in byDate)
        {
            list.Sort(CompareUpcoming);
            var kind = date == today ? DayGroupKind.Today : date == today.AddDays(1) ? DayGroupKind.Tomorrow : DayGroupKind.Date;
            groups.Add(new DayGroup<T>(kind, date, list));
        }

        return groups;
    }

    /// <summary>
    /// The divider's text in two parts, the second drawn dimmed: ("Open now", ""), ("Today", "Sunday"),
    /// ("Tomorrow", "Monday"), or the date in the Settings format and the weekday ("17 Nov", "Tuesday").
    /// </summary>
    public static (string Main, string Dimmed) Heading<T>(DayGroup<T> group, DateFormat format, CultureInfo locale)
    {
        var weekday = group.Date.ToString("dddd", CultureInfo.InvariantCulture);
        return group.Kind switch
        {
            DayGroupKind.OpenNow => ("Open now", ""),
            DayGroupKind.Today => ("Today", weekday),
            DayGroupKind.Tomorrow => ("Tomorrow", weekday),
            _ => (DisplayFormats.FormatDayMonth(group.Date, format, locale), weekday),
        };
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    private static int CompareOpenNow<T>(BrowserEntry<T> a, BrowserEntry<T> b)
    {
        var byAlways = a.AlwaysOpen.CompareTo(b.AlwaysOpen);
        if (byAlways != 0)
            return byAlways;

        // Soonest to close first; an opening with no known close after those with one
        var aClose = SortingClose(a.When);
        var bClose = SortingClose(b.When);
        var byKnown = (aClose is null).CompareTo(bClose is null);
        if (byKnown != 0)
            return byKnown;
        var byClose = Nullable.Compare(aClose, bClose);
        if (byClose != 0)
            return byClose;

        return CompareUpcoming(a, b);
    }

    private static DateTimeOffset? SortingClose(Occurrence when) => when.End ?? when.Start + AssumedLength;

    private static int CompareUpcoming<T>(BrowserEntry<T> a, BrowserEntry<T> b)
    {
        var byStart = Nullable.Compare(a.When.Start, b.When.Start);
        if (byStart != 0)
            return byStart;
        var byTitle = StringComparer.CurrentCultureIgnoreCase.Compare(a.Title, b.Title);
        return byTitle != 0 ? byTitle : string.CompareOrdinal(a.Key, b.Key);
    }
}
