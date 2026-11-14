using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class TravelRegionTests
{
    [Fact]
    public void ACharacterCanTravelToEveryDataCentreInItsHomeWorldsRegion()
    {
        var region = TestGazetteer.Instance.TravelRegion("Gilgamesh");

        Assert.NotNull(region);
        Assert.Equal(["Aether", "Crystal", "Dynamis", "Primal"], region.DataCentres);
        Assert.True(region.IncludesWorld("Balmung"));
        Assert.True(region.IncludesDataCentre("Primal"));
    }

    [Theory]
    [InlineData("Moogle")]
    [InlineData("Bismarck")]
    [InlineData("Tonberry")]
    [InlineData("Nowhere")]
    [InlineData("")]
    [InlineData(null)]
    public void WorldsInOtherRegionsOrUnknownAreNotIncluded(string? world) =>
        Assert.False(TestGazetteer.Instance.TravelRegion("Gilgamesh")!.IncludesWorld(world));

    [Theory]
    [InlineData("Chaos")]
    [InlineData("Materia")]
    [InlineData(null)]
    public void DataCentresInOtherRegionsOrUnknownAreNotIncluded(string? dataCentre) =>
        Assert.False(TestGazetteer.Instance.TravelRegion("Gilgamesh")!.IncludesDataCentre(dataCentre));

    [Fact]
    public void NamesMatchIgnoringCaseAndSurroundingSpaces()
    {
        var region = TestGazetteer.Instance.TravelRegion(" moogle ");

        Assert.NotNull(region);
        Assert.Equal(["Chaos", "Light"], region.DataCentres);
        Assert.True(region.IncludesWorld(" lich "));
        Assert.True(region.IncludesDataCentre("LIGHT "));
    }

    [Fact]
    public void ARegionWithOneDataCentreHasOnlyThatOne() =>
        Assert.Equal(["Materia"], TestGazetteer.Instance.TravelRegion("Sophia")!.DataCentres);

    [Theory]
    [InlineData("Nowhere")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownHomeWorldHasNoRegion(string? homeWorld) =>
        Assert.Null(TestGazetteer.Instance.TravelRegion(homeWorld));

    private static TravelRegion NorthAmerica => TestGazetteer.Instance.TravelRegion("Gilgamesh")!;

    private static CachedListing Listing(ListingSource source, string? world, string? dataCentre = null) =>
        new() { Source = source, Id = "1", ListedOn = world, DataCentre = dataCentre };

    [Theory]
    [InlineData(ListingSource.InGamePartyFinder, "Balmung", true)]
    [InlineData(ListingSource.InGamePartyFinder, " balmung ", true)]
    [InlineData(ListingSource.InGamePartyFinder, "Lich", false)]
    [InlineData(ListingSource.InGamePartyFinder, null, false)]
    [InlineData(ListingSource.Xivpf, "Cuchulainn", true)]
    [InlineData(ListingSource.Xivpf, "Bismarck", false)]
    [InlineData(ListingSource.RpEventCalendar, "Balmung", true)]
    [InlineData(ListingSource.RpEventCalendar, "Moogle", false)]
    [InlineData(ListingSource.RpEventCalendar, null, false)]
    public void AListingIsInTheRegionWhenItsWorldIs(ListingSource source, string? world, bool included) =>
        Assert.Equal(included, NorthAmerica.Includes(Listing(source, world)));

    [Theory]
    [InlineData("Balmung", null, true)]
    [InlineData(null, "Crystal", true)]
    [InlineData("Lich", "Crystal", true)]
    [InlineData("Balmung", "Light", true)]
    [InlineData("Lich", "Light", false)]
    [InlineData("Nowhere", null, false)]
    [InlineData(null, null, false)]
    public void APartakeEventIsInTheRegionWhenItsWorldOrDataCentreIs(string? world, string? dataCentre, bool included) =>
        Assert.Equal(included, NorthAmerica.Includes(Listing(ListingSource.Partake, world, dataCentre)));

    [Fact]
    public void AVenueIsMatchedByItsShownAddressNotAsAListing() =>
        Assert.Throws<ArgumentException>(() => NorthAmerica.Includes(Listing(ListingSource.FfxivVenues, "Balmung")));

    private static FfxivVenues.Shown Venue(string? world) => FfxivVenues.Show(
        new CachedListing
        {
            Source = ListingSource.FfxivVenues,
            Id = "1",
            Venue = new FfxivVenues.Fields(new FfxivVenues.Location(world, "Mist", 3, 4, 0, 0, false), null, null, null),
        },
        TestGazetteer.Instance);

    [Theory]
    [InlineData("Balmung", true)]
    [InlineData("Balmun", true)] // misspelt, corrected when shown
    [InlineData("Bismarck", false)]
    [InlineData("Bismark", false)]
    [InlineData(null, false)] // no address
    public void AVenueIsInTheRegionWhenItsShownAddressIs(string? world, bool included) =>
        Assert.Equal(included, NorthAmerica.Includes(Venue(world)));
}
