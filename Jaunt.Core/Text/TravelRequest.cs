using System.Globalization;

namespace Jaunt.Core.Text;

/// <summary>
/// The arguments of Lifestream's <c>BuildAddressBookEntry</c> IPC for one address: world, district,
/// ward, plot or apartment number, and whether it is an apartment in the subdivision.
/// </summary>
/// <param name="District">The district's English name, which Lifestream matches by its first word.</param>
/// <param name="Number">The plot, or the apartment number when <paramref name="IsApartment"/>.</param>
/// <param name="IsSubdivision">
/// Only for apartments: a plot's number already says whether it is in the subdivision (31 to 60).
/// </param>
public sealed record TravelRequest(string World, string District, string Ward, string Number, bool IsApartment, bool IsSubdivision)
{
    /// <summary>
    /// The request for an address, or null when Lifestream can't travel there: the world or district
    /// is unknown, or there is neither a plot nor an apartment. A room inside a house is not sent;
    /// travel arrives at the house.
    /// </summary>
    public static TravelRequest? From(HousingAddress address)
    {
        if (address.World is not { Length: > 0 } world || address.District is not { } district)
            return null;

        var ward = address.Ward.ToString(CultureInfo.InvariantCulture);
        if (address.Plot is { } plot)
            return new(world, AddressText.Name(district), ward, plot.ToString(CultureInfo.InvariantCulture), IsApartment: false, IsSubdivision: false);
        if (address.Apartment is { } apartment)
            return new(world, AddressText.Name(district), ward, apartment.ToString(CultureInfo.InvariantCulture), IsApartment: true, IsSubdivision: address.Subdivision == true);
        return null;
    }
}
