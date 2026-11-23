using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Browser;

/// <summary>
/// Ads' opening times and addresses parsed from copies of their fields, so the listing browser can
/// parse a whole cache off the game's thread before its first list rather than in one frame.
/// </summary>
public static class AdParses
{
    /// <summary>An ad's fields the parsers read, copied on the thread that owns the cache.</summary>
    public sealed record Ad(ListingSource Source, string Id, string Description, string? ListedOn, DateTimeOffset LastSeen, DateTimeOffset? FirstSeen)
    {
        public static Ad Of(CachedListing listing) =>
            new(listing.Source, listing.Id, listing.Description, listing.ListedOn, listing.LastSeen, listing.FirstSeen);
    }

    /// <summary>
    /// An ad's opening times (<see cref="Schedules.ParseAd(NormalisedText, DateTimeOffset)"/>) and its
    /// addresses and links (<see cref="ListingDetails.Parse(AddressParser, string, NormalisedText, string?)"/>),
    /// or, when it lists nothing anywhere (<see cref="Duplicates.ListsNothingAnywhere"/>), null details, as
    /// the browser doesn't read those, and until when it goes on listing nothing (<see cref="QuietUntil"/>).
    /// </summary>
    public sealed record Parse(Ad Ad, IReadOnlyList<OpeningTime> Times, ListingDetails? Details, DateTimeOffset? QuietUntil);

    /// <summary>
    /// Until when an ad that lists nothing anywhere at <paramref name="now"/> goes on listing nothing, so the
    /// browser can pass over it until then: the first time its schedule may read differently
    /// (<see cref="ListChanges.Next"/>) or it stops counting as listed (<see cref="Schedules.ListedUntil"/>),
    /// whichever is sooner; <see cref="DateTimeOffset.MaxValue"/> if neither comes. Seeing the ad again
    /// changes its text or last-seen time, which the caller checks.
    /// </summary>
    public static DateTimeOffset QuietUntil(ListingSource source, DateTimeOffset lastSeen, int cooldownMinutes, ListingSchedule schedule, DateTimeOffset now) =>
        ListChanges.Earliest(ListChanges.Next(schedule, now), Schedules.ListedUntil(source, lastSeen, cooldownMinutes), now) ?? DateTimeOffset.MaxValue;

    /// <summary>Parses each ad as the browser would at <paramref name="now"/>; safe to run on any thread.</summary>
    /// <param name="region">The region the ads were seen in, for the default zone of times stating none.</param>
    /// <param name="cooldownMinutes">The refresh cooldown, which sets how long an ad counts as still listed.</param>
    public static IReadOnlyList<Parse> Run(IReadOnlyList<Ad> ads, AddressParser parser, PartyFinderRegion region, int cooldownMinutes, DateTimeOffset now)
    {
        var parses = new List<Parse>(ads.Count);
        foreach (var ad in ads)
        {
            var normalised = PartyFinderText.Normalise(ad.Description);
            var times = Schedules.ParseAd(normalised, ad.LastSeen);
            var listed = Schedules.StillListed(ad.Source, ad.LastSeen, now, cooldownMinutes);
            var schedule = Schedules.ForAd(times, ad.LastSeen, region, listed, now, firstSeen: ad.FirstSeen);
            parses.Add(Duplicates.ListsNothingAnywhere(times, schedule, now)
                ? new Parse(ad, times, null, QuietUntil(ad.Source, ad.LastSeen, cooldownMinutes, schedule, now))
                : new Parse(ad, times, ListingDetails.Parse(parser, ad.Description, normalised, ad.ListedOn), null));
        }

        return parses;
    }
}
