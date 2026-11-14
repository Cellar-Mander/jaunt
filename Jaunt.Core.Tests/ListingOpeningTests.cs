using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ListingOpeningTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly ListingOpening Past = new(Now.AddDays(-1), Now.AddDays(-1).AddHours(3));
    private static readonly ListingOpening Current = new(Now.AddHours(-1), Now.AddHours(2));
    private static readonly ListingOpening Later = new(Now.AddDays(1), Now.AddDays(1).AddHours(3));

    [Fact]
    public void TheNextIsTheOneInProgress() => Assert.Same(Current, ListingOpening.Next([Past, Current, Later], Now));

    [Fact]
    public void OtherwiseTheNextToStart() => Assert.Same(Later, ListingOpening.Next([Past, Later], Now));

    [Fact]
    public void AnOpeningEndingNowIsOver() =>
        Assert.Same(Later, ListingOpening.Next([new ListingOpening(Now.AddHours(-3), Now), Later], Now));

    [Fact]
    public void NoneWhenAllAreOver() => Assert.Null(ListingOpening.Next([Past], Now));
}
