using System.Globalization;
using System.Text;

namespace Jaunt.Core.Text;

/// <summary>Writes a housing address as text to copy, in the shape Lifestream's address book reads.</summary>
public static class AddressText
{
    /// <summary>The district's English name, as the game writes it.</summary>
    public static string Name(District district) => district switch
    {
        District.Mist => "Mist",
        District.LavenderBeds => "The Lavender Beds",
        District.Goblet => "The Goblet",
        District.Shirogane => "Shirogane",
        District.Empyreum => "Empyreum",
        _ => district.ToString(),
    };

    /// <summary>
    /// "World, District, W20, P28", leaving out a world or district the ad doesn't give. An apartment
    /// is "W5, Subdivision, Apartment 12" (Subdivision only when the ad says so), and a room is added
    /// as ", Room B1".
    /// </summary>
    public static string Format(HousingAddress address)
    {
        var text = new StringBuilder();
        if (address.World is { Length: > 0 } world)
            Part(text, world);
        if (address.District is { } district)
            Part(text, Name(district));
        Part(text, string.Create(CultureInfo.InvariantCulture, $"W{address.Ward}"));
        if (address.Plot is { } plot)
            Part(text, string.Create(CultureInfo.InvariantCulture, $"P{plot}"));
        if (address.Apartment is { } apartment)
        {
            if (address.Subdivision == true)
                Part(text, "Subdivision");
            Part(text, string.Create(CultureInfo.InvariantCulture, $"Apartment {apartment}"));
        }

        if (address.Room is { } room)
            Part(text, "Room " + room);
        return text.ToString();
    }

    private static void Part(StringBuilder text, string part)
    {
        if (text.Length > 0)
            text.Append(", ");
        text.Append(part);
    }
}
