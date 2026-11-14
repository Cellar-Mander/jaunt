namespace Jaunt.Core.Listings;

/// <summary>
/// When the listing browser fetches a source: on opening and on Refresh, unless the source was
/// fetched within its cooldown, in which case it shows the cached listings.
/// </summary>
/// <remarks>
/// A source's cooldown is the Settings cooldown or the source's own minimum, whichever is longer.
/// The in-game Party Finder is never fetched: its listings arrive only while the player has it open.
/// </remarks>
public static class RefreshPolicy
{
    public const int DefaultCooldownMinutes = 10;

    /// <summary>Also xivpf's minimum, so the setting alone governs it.</summary>
    public const int MinCooldownMinutes = 10;

    public const int MaxCooldownMinutes = 24 * 60;

    /// <summary>The sources that are fetched, in the order the Settings help text lists them.</summary>
    public static IReadOnlyList<ListingSource> FetchedSources { get; } =
        [ListingSource.Xivpf, ListingSource.FfxivVenues, ListingSource.Partake, ListingSource.RpEventCalendar];

    /// <summary>The source's own minimum time between fetches, or null for a source that is never fetched.</summary>
    public static TimeSpan? Minimum(ListingSource source) => source switch
    {
        ListingSource.InGamePartyFinder => null,
        ListingSource.Xivpf => TimeSpan.FromMinutes(MinCooldownMinutes),
        // Schedules and events change little within a day; the RP calendar also asks for few fetches
        ListingSource.FfxivVenues or ListingSource.Partake or ListingSource.RpEventCalendar => TimeSpan.FromHours(8),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    /// <summary>
    /// Whether the source is asked for one data centre per request, each with its own cooldown, so a
    /// character in another region can fetch its data centres at once. Sources that can't be asked for
    /// less than every region have one cooldown for all.
    /// </summary>
    public static bool ByDataCentre(ListingSource source) => source == ListingSource.FfxivVenues;

    /// <summary>The time the source must wait between fetches, given the Settings cooldown.</summary>
    public static TimeSpan? Cooldown(ListingSource source, int settingMinutes)
    {
        var minimum = Minimum(source);
        if (minimum is null)
            return null;

        var setting = TimeSpan.FromMinutes(settingMinutes);
        // Stryker disable once Equality : when the two are equal, either one is the answer
        return setting > minimum ? setting : minimum;
    }

    /// <summary>Whether opening the browser or Refresh should fetch the source now.</summary>
    /// <param name="lastFetched">
    /// When the source was last fetched, or null if never. A time after <paramref name="now"/> means
    /// the clock was moved back, so how long it has been can't be told; it fetches rather than
    /// leaving the source stuck until the clock catches up.
    /// </param>
    public static bool ShouldFetch(ListingSource source, int settingMinutes, DateTimeOffset? lastFetched, DateTimeOffset now)
    {
        var cooldown = Cooldown(source, settingMinutes);
        if (cooldown is null)
            return false;

        return lastFetched is null || lastFetched.Value > now || now - lastFetched.Value >= cooldown.Value;
    }
}
