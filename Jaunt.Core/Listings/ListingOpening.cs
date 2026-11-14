namespace Jaunt.Core.Listings;

/// <summary>One time a listing is open, as its source gives it.</summary>
/// <param name="End">After <paramref name="Start"/>.</param>
public sealed record ListingOpening(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>The opening in progress at <paramref name="now"/>, or else the next to start; null if none is left.</summary>
    /// <param name="openings">Earliest first, as the cache keeps them.</param>
    public static ListingOpening? Next(IEnumerable<ListingOpening> openings, DateTimeOffset now) =>
        openings.FirstOrDefault(o => o.End > now);
}
