namespace Jaunt.Core.Listings;

/// <summary>One time a listing is open, as its source gives it.</summary>
/// <param name="End">After <paramref name="Start"/>.</param>
public sealed record ListingOpening(DateTimeOffset Start, DateTimeOffset End);
