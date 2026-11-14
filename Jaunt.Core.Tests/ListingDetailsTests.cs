using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class ListingDetailsTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("The Velvet Room", "The Velvet Room")]
    [InlineData("  The Velvet Room  \rSiren Mist W5 P12", "The Velvet Room")]
    [InlineData(" Bar\rOpen now", "ABC Bar")] // boxed letters decoded
    [InlineData("★★★\rThe Velvet Room", "The Velvet Room")] // a line of decoration only is dropped
    public void TitleIsTheFirstLine(string ad, string expected) =>
        Assert.Equal(expected, ListingDetails.Title(ad));

    [Fact]
    public void ATitleOfExactlyTheLimitIsKept()
    {
        var line = new string('a', ListingDetails.MaxTitleLength);
        Assert.Equal(line, ListingDetails.Title(line));
    }

    [Fact]
    public void ALongerTitleIsCutWithAnEllipsis()
    {
        var title = ListingDetails.Title(new string('a', ListingDetails.MaxTitleLength - 2) + " bcd");
        Assert.Equal(new string('a', ListingDetails.MaxTitleLength - 2) + "…", title);
        Assert.Equal(ListingDetails.MaxTitleLength - 1, title.Length);
    }

    [Fact]
    public void ParsesAddressesAndLinks()
    {
        var details = ListingDetails.Parse(TestGazetteer.Parser, "Velvet Room Mist W5 P12 discord.gg/velvet", "Siren");

        var address = Assert.Single(details.Addresses);
        Assert.Equal(new HousingAddress("Siren", WorldSource.Listing, District.Mist, 5, 12), address);
        Assert.Equal(new Uri("https://discord.gg/velvet"), Assert.Single(details.Links).Uri);
    }
}
