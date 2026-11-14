using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class TravelRequestTests
{
    [Fact]
    public void AHouseSendsItsPlot() =>
        Assert.Equal(
            new TravelRequest("Siren", "The Lavender Beds", "20", "28", IsApartment: false, IsSubdivision: false),
            TravelRequest.From(new("Siren", WorldSource.Text, District.LavenderBeds, 20, 28, Subdivision: true, Room: "3")));

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AnApartmentSendsItsNumberAndSubdivision(bool? subdivision, bool expected) =>
        Assert.Equal(
            new TravelRequest("Lich", "Empyreum", "7", "41", IsApartment: true, IsSubdivision: expected),
            TravelRequest.From(new("Lich", WorldSource.Listing, District.Empyreum, 7, null, subdivision, Apartment: 41)));

    [Fact]
    public void APlotWinsOverAnApartment() =>
        Assert.Equal("12", TravelRequest.From(new("Lich", WorldSource.Text, District.Mist, 7, 12, Apartment: 41))!.Number);

    public static TheoryData<HousingAddress> Untravellable => new()
    {
        new(null, WorldSource.Listing, District.Mist, 5, 3),
        new("", WorldSource.Listing, District.Mist, 5, 3),
        new("Lich", WorldSource.Text, null, 5, 3),
        new("Lich", WorldSource.Text, District.Mist, 5, null),
        new("Lich", WorldSource.Text, District.Mist, 5, null, Room: "3"),
    };

    [Theory]
    [MemberData(nameof(Untravellable))]
    public void NoRequestWithoutWorldDistrictAndPlotOrApartment(HousingAddress address) =>
        Assert.Null(TravelRequest.From(address));
}
