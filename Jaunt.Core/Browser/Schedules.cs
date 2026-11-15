using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>One time a listing is open, in UTC.</summary>
/// <param name="Start">When it opens; null when it is open now but the source doesn't say since when.</param>
/// <param name="End">When it closes; null when the source doesn't say.</param>
public sealed record Occurrence(DateTimeOffset? Start, DateTimeOffset? End)
{
    /// <summary>Whether it is open at <paramref name="now"/>: started (or open since an unknown time) and not yet ended.</summary>
    public bool IsOpen(DateTimeOffset now) => (Start is null || Start <= now) && (End is null || End > now);
}

/// <summary>
/// When a listing is open, as the browser reads it from its source.
/// </summary>
/// <param name="AlwaysOpen">Open around the clock; <paramref name="Occurrences"/> is then empty.</param>
/// <param name="OneOff">Single events, each listed up to <see cref="Schedules.OneOffHorizon"/> ahead, rather than a recurring opening.</param>
/// <param name="Occurrences">Earliest first.</param>
public sealed record ListingSchedule(bool AlwaysOpen, bool OneOff, IReadOnlyList<Occurrence> Occurrences)
{
    public static readonly ListingSchedule None = new(false, false, []);

    public static readonly ListingSchedule Always = new(true, false, []);
}

/// <summary>
/// Works out each source's openings and which of them the browser lists: a recurring listing's
/// current opening and the next one, a one-off event's openings starting within 30 days, and a 24/7
/// venue once, as open now.
/// </summary>
public static class Schedules
{
    /// <summary>How far ahead a one-off event is listed.</summary>
    public static readonly TimeSpan OneOffHorizon = TimeSpan.FromDays(30);

    /// <summary>
    /// The openings to list at <paramref name="now"/>, earliest first. For a recurring listing, the
    /// one in progress and the next after it, or only the next when none is in progress. For one-offs,
    /// each that hasn't ended and starts within <see cref="OneOffHorizon"/>. For 24/7, one open
    /// occurrence with no start or end.
    /// </summary>
    public static IReadOnlyList<Occurrence> Listed(ListingSchedule schedule, DateTimeOffset now)
    {
        if (schedule.AlwaysOpen)
            return [new Occurrence(null, null)];

        var upcoming = schedule.Occurrences.Where(o => o.End is null || o.End > now);
        if (schedule.OneOff)
            return [.. upcoming.Where(o => o.Start is null || o.Start <= now + OneOffHorizon)];

        var listed = new List<Occurrence>(2);
        foreach (var occurrence in upcoming)
        {
            listed.Add(occurrence);
            if (!listed[0].IsOpen(now) || listed.Count == 2)
                break;
        }

        return listed;
    }

    /// <summary>A Partake event: one-off, from its start and end.</summary>
    public static ListingSchedule ForEvent(DateTimeOffset? start, DateTimeOffset? end) =>
        start is { } from && end is { } to ? new ListingSchedule(false, true, [new Occurrence(from, to)]) : ListingSchedule.None;

    /// <summary>An event with occurrences, such as from the FFXIV RP Event Calendar.</summary>
    /// <param name="openings">Earliest first.</param>
    public static ListingSchedule ForOpenings(IReadOnlyList<ListingOpening> openings, bool oneOff) =>
        new(false, oneOff, [.. openings.Select(o => new Occurrence(o.Start, o.End))]);

    /// <summary>
    /// An FFXIV Venues venue. Each schedule entry's occurrence as the API resolved it is carried forward
    /// by its interval (every N weeks) until it is current, so a venue fetched days ago still shows its
    /// right openings; an entry repeating by week of the month, which can't be stepped by a fixed
    /// period, keeps only the resolved occurrence. Stepping is in whole weeks of UTC, so after a
    /// daylight saving change an opening is an hour out until the venue is fetched again.
    /// </summary>
    public static ListingSchedule ForVenue(FfxivVenues.Fields? fields, DateTimeOffset now)
    {
        if (FfxivVenues.AlwaysOpen(fields?.Schedule))
            return ListingSchedule.Always;

        var occurrences = new List<Occurrence>();
        foreach (var entry in fields?.Schedule ?? [])
        {
            if (entry?.Resolution is not { } resolved || resolved.End <= resolved.Start)
                continue;

            var start = resolved.Start;
            var end = resolved.End;
            if (entry.Interval is { IntervalType: 0, IntervalArgument: > 0 and var weeks })
            {
                var period = TimeSpan.FromDays(7 * weeks);
                if (end <= now)
                {
                    var steps = (long)Math.Ceiling((now - end) / period);
                    // An end exactly at now is over, so step once more
                    if (end + (steps * period) <= now)
                        steps++;
                    start += steps * period;
                    end += steps * period;
                }

                occurrences.Add(new Occurrence(start, end));
                occurrences.Add(new Occurrence(start + period, end + period));
            }
            else if (end > now)
            {
                occurrences.Add(new Occurrence(start, end));
            }
        }

        occurrences.Sort((a, b) => Nullable.Compare(a.Start, b.Start));
        return new ListingSchedule(false, false, occurrences);
    }

    /// <summary>
    /// A Party Finder ad, from the opening times in its text (see "Opening times in Party Finder ads"
    /// in <c>docs/design.md</c>). An ad that says it is open now, is 24/7 or gives no time is open only
    /// while it is still listed (decision <c>ab014655</c>); a stated time stands whether or not it is.
    /// Its openings are one-offs.
    /// </summary>
    /// <param name="adText">The ad's text as the game sends it.</param>
    /// <param name="seen">When the ad was last seen, which "today", weekdays and undated times count from.</param>
    /// <param name="region">The region the ad was seen in, for its default time zone.</param>
    /// <param name="listed">Whether the ad is still listed at <paramref name="now"/>.</param>
    public static ListingSchedule ForAd(string adText, DateTimeOffset seen, PartyFinderRegion region, bool listed, DateTimeOffset now) =>
        ForAd(ParseAd(adText, seen), seen, region, listed, now);

    /// <summary>The opening times an ad's text states, for <see cref="ForAd(IReadOnlyList{OpeningTime}, DateTimeOffset, PartyFinderRegion, bool, DateTimeOffset)"/>.</summary>
    /// <param name="seen">When the ad was last seen; its UTC date picks the year and reading of a date like 7/11.</param>
    public static IReadOnlyList<OpeningTime> ParseAd(string adText, DateTimeOffset seen) =>
        OpeningTimes.Parse(PartyFinderText.Normalise(adText).Text, DateOnly.FromDateTime(seen.UtcDateTime));

    /// <summary>As <see cref="ForAd(string, DateTimeOffset, PartyFinderRegion, bool, DateTimeOffset)"/>, from times already parsed with <see cref="ParseAd"/>.</summary>
    /// <param name="unstatedZone">
    /// The zone for times that state none (or one not known), instead of the region default: the zone
    /// of a venue or event at the ad's address (<see cref="Duplicates.UnstatedZone"/>).
    /// </param>
    /// <param name="firstSeen">
    /// When the ad was first seen with this text (<see cref="CachedListing.FirstSeen"/>), which a closing
    /// time on its own counts from, so it stays put while the ad stays listed; null counts from
    /// <paramref name="seen"/>.
    /// </param>
    public static ListingSchedule ForAd(
        IReadOnlyList<OpeningTime> times, DateTimeOffset seen, PartyFinderRegion region, bool listed, DateTimeOffset now,
        TimeZoneInfo? unstatedZone = null, DateTimeOffset? firstSeen = null)
    {
        var occurrences = new List<Occurrence>();
        var timed = false;
        foreach (var time in times)
        {
            if (time.Always)
                return listed ? ListingSchedule.Always : ListingSchedule.None;

            // The ad's language isn't known, so a European ad without a zone takes London's
            var zone = OpeningTimeResolver.Written(time.Zone) ?? unstatedZone ?? OpeningTimeResolver.Zone(null, region, language: null);
            // "Open until 12am" seen again after midnight is over, not open until the next midnight
            var window = OpeningTimeResolver.Resolve(time, time.Start is null ? firstSeen ?? seen : seen, zone);
            if (window.Start is { } start)
            {
                timed = true;
                // With no closing time, a started opening lasts only while the ad is listed
                if (window.End is not null || start > now || listed)
                    occurrences.Add(new Occurrence(start, window.End));
            }
            else if (window.End is { } end)
            {
                // A closing time alone belongs to an opening already under way ("open until 1am")
                timed = true;
                if (listed)
                    occurrences.Add(new Occurrence(null, end));
            }
        }

        // "Open now" with no time, or no time at all, is open while listed
        if (!timed && listed)
            occurrences.Add(new Occurrence(null, null));

        occurrences.Sort((a, b) => Nullable.Compare(a.Start, b.Start));
        return new ListingSchedule(false, true, occurrences);
    }

    /// <summary>How long a Party Finder ad stays up unless its recruiter renews it.</summary>
    public static readonly TimeSpan AdLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether a Party Finder ad last seen at <paramref name="lastSeen"/> may still be listed: within
    /// <see cref="AdLifetime"/>, plus for a fetched source its cooldown, since the ad can't be seen
    /// again until the next fetch.
    /// </summary>
    public static bool StillListed(ListingSource source, DateTimeOffset lastSeen, DateTimeOffset now, int cooldownMinutes) =>
        now < ListedUntil(source, lastSeen, cooldownMinutes);

    /// <summary>When a Party Finder ad last seen at <paramref name="lastSeen"/> stops counting as listed (<see cref="StillListed"/>).</summary>
    public static DateTimeOffset ListedUntil(ListingSource source, DateTimeOffset lastSeen, int cooldownMinutes) =>
        lastSeen + AdLifetime + (RefreshPolicy.Cooldown(source, cooldownMinutes) ?? TimeSpan.Zero);

    /// <summary>The Party Finder region a data centre is in, or null for one this doesn't know.</summary>
    public static PartyFinderRegion? RegionOf(string dataCentre) => dataCentre.Trim().ToUpperInvariant() switch
    {
        "AETHER" or "CRYSTAL" or "DYNAMIS" or "PRIMAL" => PartyFinderRegion.NorthAmerica,
        "CHAOS" or "LIGHT" or "SHADOW" => PartyFinderRegion.Europe,
        "MATERIA" => PartyFinderRegion.Oceania,
        "ELEMENTAL" or "GAIA" or "MANA" or "METEOR" => PartyFinderRegion.Japan,
        _ => null,
    };
}
