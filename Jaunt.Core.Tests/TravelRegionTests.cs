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
}
