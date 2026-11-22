using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>
/// A housing address as duplicates are matched on: world (case ignored), district, ward, and plot or
/// apartment, or neither for an address that names only the ward. A room inside the house is left out;
/// so is whether a plot is in the subdivision, which its number already says.
/// </summary>
public readonly record struct AddressKey(string World, District District, int Ward, int? Plot, int? Apartment, bool Subdivision)
{
    /// <summary>Names only the ward, not which plot or apartment in it.</summary>
    public bool WardOnly => Plot is null && Apartment is null;

    /// <summary>The ward-only key for the ward this address is in.</summary>
    public AddressKey InWard => new(World, District, Ward, null, null, false);

    // The generated one prints every property, and InWard has an InWard, without end
    public override string ToString() =>
        $"{World} {District} W{Ward}" + (Plot is { } plot ? $" P{plot}" : "") + (Apartment is { } apartment ? $" A{apartment}{(Subdivision ? " sub" : "")}" : "");

    /// <summary>The key for an address, or null when it lacks a world or a district.</summary>
    public static AddressKey? Of(HousingAddress address)
    {
        if (address.World is not { } world || address.District is not { } district)
            return null;
        if (address.Plot is { } plot)
            // Stryker disable once Boolean : every plot key has the same Subdivision, so its value never tells two apart
            return new AddressKey(world.ToUpperInvariant(), district, address.Ward, plot, null, false);
        if (address.Apartment is { } apartment)
            return new AddressKey(world.ToUpperInvariant(), district, address.Ward, null, apartment, address.Subdivision == true);
        return new AddressKey(world.ToUpperInvariant(), district, address.Ward, null, null, false);
    }
}

/// <summary>One listing as the browser would list it, for <see cref="Duplicates.Merge"/>.</summary>
/// <param name="Item">The listing, as the caller describes it.</param>
/// <param name="Address">Its first address's key; null never merges.</param>
/// <param name="Listed">The openings the browser lists for it (<see cref="Schedules.Listed"/>), earliest first.</param>
/// <param name="AdText">An ad's text, from the game, xivpf or a shout; null for other sources. Blank text never merges.</param>
public sealed record MergeCandidate<T>(T Item, AddressKey? Address, ListingSchedule Schedule, IReadOnlyList<Occurrence> Listed, string? AdText = null);

/// <summary>Listings shown as one entry.</summary>
/// <param name="Members">In the order they were given; one for a listing with no duplicate.</param>
/// <param name="Schedule">
/// 24/7 when any member is; one-off only when every member is. Its occurrences are <see cref="Listed"/>.
/// </param>
/// <param name="Listed">Every member's listed openings, overlapping ones combined, earliest first.</param>
public sealed record MergedListing<T>(IReadOnlyList<T> Members, ListingSchedule Schedule, IReadOnlyList<Occurrence> Listed);

/// <summary>
/// Finds listings that are the same thing listed more than once, in one source or several: the same
/// address (<see cref="AddressKey"/>) with listed openings that overlap, or Party Finder ads with exactly
/// the same text, as a re-post or the xivpf copy of an ad gets a new ID. A name never decides a match
/// (decision <c>25e33e0a</c>). See "Duplicates" under "Listing browser" in <c>docs/design.md</c>.
/// </summary>
public static class Duplicates
{
    /// <summary>
    /// Groups the candidates into entries. Two candidates at one address are one entry when any of
    /// their listed openings overlap, or either is 24/7; the grouping is transitive. A candidate whose
    /// address names only the ward also joins the plot or apartment in that ward it overlaps, when
    /// there is exactly one, as a busy ward can have several venues open at once. Ads with exactly the
    /// same non-blank text are one entry whatever their address or openings. Candidates at an address or
    /// in a ward are compared pairwise, which stays small as each has a handful of listings at most.
    /// </summary>
    /// <param name="now">An opening without a start is open now, so it counts as starting now.</param>
    public static IReadOnlyList<MergedListing<T>> Merge<T>(IReadOnlyList<MergeCandidate<T>> candidates, DateTimeOffset now)
    {
        var parent = new int[candidates.Count];
        var byAddress = new Dictionary<AddressKey, List<int>>();
        var byText = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++)
        {
            parent[i] = i;
            if (candidates[i].AdText is { } text && !string.IsNullOrWhiteSpace(text) && !byText.TryAdd(text, i))
                parent[Root(parent, i)] = Root(parent, byText[text]);

            if (candidates[i].Address is not { } key)
                continue;
            if (!byAddress.TryGetValue(key, out var others))
                byAddress[key] = others = [];

            foreach (var j in others)
            {
                if (Overlap(candidates[i], candidates[j], now))
                    parent[Root(parent, i)] = Root(parent, j);
            }

            others.Add(i);
        }

        JoinWardOnly(candidates, parent, byAddress, now);

        var members = new Dictionary<int, List<int>>();
        var order = new List<int>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var root = Root(parent, i);
            if (!members.TryGetValue(root, out var group))
            {
                members[root] = group = [];
                order.Add(root);
            }

            group.Add(i);
        }

        var merged = new List<MergedListing<T>>(order.Count);
        foreach (var root in order)
        {
            var group = members[root];
            if (group.Count == 1)
            {
                var only = candidates[group[0]];
                merged.Add(new MergedListing<T>([only.Item], only.Schedule, only.Listed));
                continue;
            }

            merged.Add(Combine(group.Select(i => candidates[i]).ToList(), now));
        }

        return merged;
    }

    /// <summary>
    /// The zone for an ad's times that state none, from the listings at its address: an FFXIV Venues
    /// venue's own zone, else the zone in which one of the ad's unzoned start times falls exactly on a
    /// listed start there (such as a Partake event's, which comes in UTC with no zone). Null when
    /// neither gives one, so the region default applies.
    /// </summary>
    /// <param name="times">The ad's opening times as written.</param>
    /// <param name="seen">When the ad was last seen.</param>
    /// <param name="venueZone">The zone of an FFXIV Venues venue at the address, or null.</param>
    /// <param name="starts">Starts listed at the address by venues and events, in UTC.</param>
    public static TimeZoneInfo? UnstatedZone(IReadOnlyList<OpeningTime> times, DateTimeOffset seen, TimeZoneInfo? venueZone, IReadOnlyCollection<DateTimeOffset> starts)
    {
        var unzoned = times.Where(Unzoned).ToList();
        if (unzoned.Count == 0)
            return null;
        if (venueZone is not null)
            return venueZone;
        if (starts.Count == 0)
            return null;

        foreach (var zone in OpeningTimeResolver.WritableZones)
        {
            foreach (var time in unzoned)
            {
                if (OpeningTimeResolver.Resolve(time, seen, zone).Start is { } start && starts.Contains(start))
                    return zone;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether an ad whose schedule lists nothing at <paramref name="now"/> would list nothing at any
    /// address: none of its start times lacks a zone, so <see cref="UnstatedZone"/> can't move them.
    /// The browser drops such an ad before parsing its addresses.
    /// </summary>
    /// <param name="times">The ad's opening times as written.</param>
    /// <param name="schedule">Its schedule in the region's default zone (<see cref="Schedules.ForAd(IReadOnlyList{OpeningTime}, DateTimeOffset, PartyFinderRegion, bool, DateTimeOffset, TimeZoneInfo?, DateTimeOffset?)"/>).</param>
    public static bool ListsNothingAnywhere(IReadOnlyList<OpeningTime> times, ListingSchedule schedule, DateTimeOffset now) =>
        Schedules.Listed(schedule, now).Count == 0 && !times.Any(Unzoned);

    // A start time an address's zone would read in place of the region default
    private static bool Unzoned(OpeningTime time) => time.Start is not null && OpeningTimeResolver.Written(time.Zone) is null;

    /// <summary>
    /// A merged entry's links: each member's in turn, the same URI once. The FFXIV RP Event Calendar's
    /// forwarder links (<see cref="RpEventCalendar.IsForwarder"/>) are left out when another member gives
    /// a page, as they forward to the venue's page, which would then show again under Location.
    /// </summary>
    /// <param name="byMember">Each member's links, in the order the entry's title ranks them.</param>
    public static IReadOnlyList<WebLink> Links(IEnumerable<IReadOnlyList<WebLink>> byMember)
    {
        var links = new List<WebLink>();
        var seen = new HashSet<Uri>();
        foreach (var link in byMember.SelectMany(l => l))
        {
            if (seen.Add(link.Uri))
                links.Add(link);
        }

        if (links.Exists(l => !RpEventCalendar.IsForwarder(l.Uri) && !WebLinks.IsDiscordInvite(l.Uri)))
            links.RemoveAll(l => RpEventCalendar.IsForwarder(l.Uri));
        return links;
    }

    /// <summary>
    /// Whose title a merged entry shows: FFXIV Venues' name first (decision <c>25e33e0a</c>), then
    /// events', then a Party Finder ad's, then a shout's, which is one long line.
    /// </summary>
    public static int TitleRank(ListingSource source) => source switch
    {
        ListingSource.FfxivVenues => 0,
        ListingSource.Partake => 1,
        ListingSource.RpEventCalendar => 2,
        ListingSource.Shout => 4,
        _ => 3,
    };

    /// <summary>
    /// Whose text a merged entry's About and lineup take first: a shout's, which is usually longer than
    /// a Party Finder ad and about the opening at hand; then other ads' and events'; a venue's standing
    /// description last. Ties go to the one that stops being listed soonest.
    /// </summary>
    public static int AboutRank(ListingSource source) => source switch
    {
        ListingSource.Shout => 0,
        ListingSource.FfxivVenues => 2,
        _ => 1,
    };

    /// <summary>Whose addresses a merged entry shows: an ad's first, as it can name the room, then the structured sources'.</summary>
    public static int AddressRank(ListingSource source) => source switch
    {
        ListingSource.InGamePartyFinder or ListingSource.Xivpf or ListingSource.Shout => 0,
        ListingSource.FfxivVenues => 1,
        ListingSource.RpEventCalendar => 2,
        _ => 3,
    };

    /// <summary>Joins each ward-only candidate to the one plot or apartment in its ward that it overlaps, if only one does.</summary>
    private static void JoinWardOnly<T>(IReadOnlyList<MergeCandidate<T>> candidates, int[] parent, Dictionary<AddressKey, List<int>> byAddress, DateTimeOffset now)
    {
        var wards = new HashSet<AddressKey>(byAddress.Keys.Where(k => k.WardOnly));
        var specificInWard = byAddress.Where(p => !p.Key.WardOnly && wards.Contains(p.Key.InWard)).ToLookup(p => p.Key.InWard);
        foreach (var ward in wards)
        {
            foreach (var i in byAddress[ward])
            {
                var overlapping = specificInWard[ward]
                    .Select(p => p.Value.Where(j => Overlap(candidates[i], candidates[j], now)).ToList())
                    .Where(js => js.Count > 0)
                    .ToList();
                if (overlapping.Count != 1)
                    continue;
                foreach (var j in overlapping[0])
                    parent[Root(parent, i)] = Root(parent, j);
            }
        }
    }

    private static int Root(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static bool Overlap<T>(MergeCandidate<T> a, MergeCandidate<T> b, DateTimeOffset now)
    {
        if (a.Schedule.AlwaysOpen || b.Schedule.AlwaysOpen)
            return true;
        foreach (var x in a.Listed)
        {
            foreach (var y in b.Listed)
            {
                if (Overlap(x, y, now))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether two openings overlap. One without an end counts as the moment it starts: inside the
    /// other's span, or the same moment as another without an end.
    /// </summary>
    internal static bool Overlap(Occurrence a, Occurrence b, DateTimeOffset now)
    {
        var aStart = a.Start ?? now;
        var bStart = b.Start ?? now;
        return (a.End, b.End) switch
        {
            ({ } aEnd, { } bEnd) => aStart < bEnd && bStart < aEnd,
            ({ } aEnd, null) => aStart <= bStart && bStart < aEnd,
            (null, { } bEnd) => bStart <= aStart && aStart < bEnd,
            _ => aStart == bStart,
        };
    }

    private static MergedListing<T> Combine<T>(List<MergeCandidate<T>> group, DateTimeOffset now)
    {
        var items = group.Select(c => c.Item).ToList();
        if (group.Any(c => c.Schedule.AlwaysOpen))
            return new MergedListing<T>(items, ListingSchedule.Always, Schedules.Listed(ListingSchedule.Always, now));

        var all = group.SelectMany(c => c.Listed).OrderBy(o => o.Start ?? now).ToList();
        var listed = new List<Occurrence>(all.Count);
        foreach (var occurrence in all)
        {
            if (listed.Count > 0 && Overlap(listed[^1], occurrence, now))
                listed[^1] = Union(listed[^1], occurrence);
            else
                listed.Add(occurrence);
        }

        var oneOff = group.All(c => c.Schedule.OneOff);
        return new MergedListing<T>(items, new ListingSchedule(false, oneOff, listed), listed);
    }

    /// <summary>Two overlapping openings as one: the earlier start (unknown if either is) and the later end.</summary>
    private static Occurrence Union(Occurrence a, Occurrence b)
    {
        DateTimeOffset? start = a.Start is { } aStart && b.Start is { } bStart ? Utc(Math.Min(aStart.UtcTicks, bStart.UtcTicks)) : null;
        DateTimeOffset? end = (a.End, b.End) switch
        {
            ({ } aEnd, { } bEnd) => Utc(Math.Max(aEnd.UtcTicks, bEnd.UtcTicks)),
            // An opening without an end lies inside the other, so the other's end is the later
            ({ } aEnd, null) => aEnd,
            (null, { } bEnd) => bEnd,
            _ => null,
        };
        return new Occurrence(start, end);
    }

    private static DateTimeOffset Utc(long ticks) => new(ticks, TimeSpan.Zero);
}
