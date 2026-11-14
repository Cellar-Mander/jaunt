using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class AddressTextTests
{
    [Theory]
    [InlineData(District.Mist, "Mist")]
    [InlineData(District.LavenderBeds, "The Lavender Beds")]
    [InlineData(District.Goblet, "The Goblet")]
    [InlineData(District.Shirogane, "Shirogane")]
    [InlineData(District.Empyreum, "Empyreum")]
    public void DistrictsHaveTheirEnglishNames(District district, string name) =>
        Assert.Equal(name, AddressText.Name(district));

    public static TheoryData<HousingAddress, string> Addresses => new()
    {
        { new("Siren", WorldSource.Text, District.Mist, 20, 28), "Siren, Mist, W20, P28" },
        { new("Siren", WorldSource.Listing, District.Goblet, 1, 60), "Siren, The Goblet, W1, P60" },
        { new(null, WorldSource.Listing, District.Empyreum, 5, 3), "Empyreum, W5, P3" },
        { new("", WorldSource.Listing, District.Empyreum, 5, 3), "Empyreum, W5, P3" },
        { new("Lich", WorldSource.Text, null, 5, 3), "Lich, W5, P3" },
        { new("Lich", WorldSource.Text, District.LavenderBeds, 12, null, Apartment: 40), "Lich, The Lavender Beds, W12, Apartment 40" },
        {
            new("Lich", WorldSource.Text, District.LavenderBeds, 12, null, Subdivision: true, Apartment: 40),
            "Lich, The Lavender Beds, W12, Subdivision, Apartment 40"
        },
        {
            new("Lich", WorldSource.Text, District.LavenderBeds, 12, null, Subdivision: false, Apartment: 40),
            "Lich, The Lavender Beds, W12, Apartment 40"
        },
        // A plot's subdivision is in its number, so it isn't written
        { new("Odin", WorldSource.Text, District.Shirogane, 8, 45, Subdivision: true), "Odin, Shirogane, W8, P45" },
        { new("Odin", WorldSource.Text, District.Shirogane, 8, 45, Room: "B1"), "Odin, Shirogane, W8, P45, Room B1" },
        { new("Odin", WorldSource.Text, District.Mist, 22, null), "Odin, Mist, W22" },
    };

    [Theory]
    [MemberData(nameof(Addresses))]
    public void FormatsAddresses(HousingAddress address, string expected) =>
        Assert.Equal(expected, AddressText.Format(address));
}
