using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class HousingMarkerTests
{
    public static TheoryData<HousingAddress, int?> Markers => new()
    {
        { new("Siren", WorldSource.Text, District.Mist, 20, 1), 0 },
        { new("Siren", WorldSource.Text, District.Mist, 20, 30), 29 },
        { new("Siren", WorldSource.Text, District.Mist, 20, 31), 30 },
        { new("Siren", WorldSource.Text, District.Mist, 20, 60), 59 },
        { new("Siren", WorldSource.Text, District.Mist, 20, 0), null },
        { new("Siren", WorldSource.Text, District.Mist, 20, 61), null },
        // The world doesn't matter, as every world's wards share one map
        { new(null, WorldSource.Listing, District.Goblet, 3, 12), 11 },
        { new("Siren", WorldSource.Text, null, 20, 28), null },
        { new("Odin", WorldSource.Text, District.Mist, 22, null), null },
        { new("Lich", WorldSource.Text, District.LavenderBeds, 12, null, Apartment: 40), 60 },
        { new("Lich", WorldSource.Text, District.LavenderBeds, 12, null, Subdivision: false, Apartment: 40), 60 },
        { new("Lich", WorldSource.Text, District.LavenderBeds, 12, null, Subdivision: true, Apartment: 40), 61 },
        // A plot wins over an apartment, as for travel, and a plot's subdivision is in its number
        { new("Lich", WorldSource.Text, District.LavenderBeds, 12, 5, Subdivision: true, Apartment: 40), 4 },
        { new("Odin", WorldSource.Text, District.Shirogane, 8, 45, Room: "B1"), 44 },
    };

    [Theory]
    [MemberData(nameof(Markers))]
    public void FindsTheMarker(HousingAddress address, int? expected) =>
        Assert.Equal(expected, HousingMarker.Index(address));
}
