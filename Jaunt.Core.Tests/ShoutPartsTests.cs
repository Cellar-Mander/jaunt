using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ShoutPartsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private const string Address = "Moonlit Lounge OPEN NOW at Mist W14 P33!";
    private const string Lineup = "DJ Saber on the decks till 1am, gamba and drinks!";

    private static string Join(ShoutParts parts, string text, double seconds, string id = "a") =>
        parts.Join(TestGazetteer.Parser, id, text, "Cuchulainn", Now.AddSeconds(seconds));

    [Theory]
    [InlineData(Address, Lineup)]
    [InlineData(Lineup, Address)]
    // Both name the place, once as the plot and once as its ward
    [InlineData(Address, "Mist ward 14 tonight, DJ Saber!")]
    [InlineData(Address, "Still open at Mist W14 P33, DJ Saber!")]
    public void TheNextPartWithinTheWindowIsReadWithTheLast(string first, string second)
    {
        var parts = new ShoutParts();
        Join(parts, first, 0);

        Assert.Equal(first + "\n" + second, Join(parts, second, 60));
    }

    [Fact]
    public void APartAfterTheWindowStandsAlone()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);

        Assert.Equal(Lineup, Join(parts, Lineup, 60.001));
    }

    [Fact]
    public void AnotherShoutersShoutIsNotAPart()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);

        Assert.Equal(Lineup, Join(parts, Lineup, 5, id: "b"));
    }

    [Fact]
    public void ARepeatOfTheLastPartIsNotJoinedToIt()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);

        Assert.Equal(Address, Join(parts, Address, 5));
    }

    [Theory]
    [InlineData("Club Nine open now, Goblet W5 P12!")]
    // Another plot in the same ward is another place
    [InlineData("Club Nine open now, Mist W14 P2!")]
    [InlineData("Club Nine open now, Mist ward 15 tonight!")]
    public void PartsNamingDifferentPlacesAreNotJoined(string second)
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);

        Assert.Equal(second, Join(parts, second, 5));
    }

    [Fact]
    public void AThirdPartJoinsTheFirstTwoAndTheWindowRunsFromTheLatest()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);
        Join(parts, Lineup, 50);

        Assert.Equal(Address + "\n" + Lineup + "\nCome dance!", Join(parts, "Come dance!", 100));
    }

    [Fact]
    public void AShoutStartsAfreshAfterOneNotJoined()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);
        Join(parts, "Club Nine open now, Goblet W5 P12!", 5);

        Assert.Equal("Club Nine open now, Goblet W5 P12!\n" + Lineup, Join(parts, Lineup, 10));
    }

    [Fact]
    public void AShoutHeardBeforeTheLastIsNotAPart()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);

        Assert.Equal(Lineup, Join(parts, Lineup, -1));
    }

    [Fact]
    public void APartHeardAtTheSameInstantIsJoined()
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);

        Assert.Equal(Address + "\n" + Lineup, Join(parts, Lineup, 0));
    }

    [Theory]
    // Others heard just after the shouter, so none is past the window when they are pruned
    [InlineData(1, 2)]
    // The part at the window's very end, when every other shouter is as old
    [InlineData(0, 60)]
    public void AmongManyShoutersEachStillJoinsTheirOwnParts(double othersAt, double partAt)
    {
        var parts = new ShoutParts();
        Join(parts, Address, 0);
        for (var i = 0; i < 300; i++)
            Join(parts, Address, othersAt, id: $"other{i}");

        Assert.Equal(Address + "\n" + Lineup, Join(parts, Lineup, partAt));
    }

    [Theory]
    // Below 256 nothing is forgotten, however old
    [InlineData(255, 256)]
    // At 256 those past the window are forgotten before the new shouter is remembered
    [InlineData(256, 1)]
    [InlineData(300, 1)]
    public void ShoutersPastTheWindowAreForgottenOnceThereAreMany(int earlier, int remembered)
    {
        var parts = new ShoutParts();
        for (var i = 0; i < earlier; i++)
            Join(parts, Address, 0, id: $"other{i}");

        Join(parts, Address, 61);

        Assert.Equal(remembered, parts.Remembered);
    }

    [Fact]
    public void ShoutersWithinTheWindowAreNotForgotten()
    {
        var parts = new ShoutParts();
        for (var i = 0; i < 300; i++)
            Join(parts, Address, 0, id: $"other{i}");

        Join(parts, Address, 60);

        Assert.Equal(301, parts.Remembered);
    }

    [Fact]
    public void AJoinedAdvertIsRecordedWithBothParts()
    {
        var cache = new ListingCache();
        var parts = new ShoutParts();
        Assert.Null(Shouts.Record(cache, TestGazetteer.Parser, "a", Join(parts, Lineup, 0), "Cuchulainn", Now));

        var listing = Shouts.Record(cache, TestGazetteer.Parser, "a", Join(parts, Address, 10), "Cuchulainn", Now.AddSeconds(10))!;

        Assert.Equal(Lineup + "\n" + Address, listing.Description);
        Assert.Equal("Saber", Assert.Single(ListingDetails.Parse(TestGazetteer.Parser, listing.Description, "Cuchulainn").Performers).Name);
    }
}
