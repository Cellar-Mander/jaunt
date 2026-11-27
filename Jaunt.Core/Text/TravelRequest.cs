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
        if (string.IsNullOrEmpty(address.World) || !address.District.HasValue)
            return null;

        var district = AddressText.Name(address.District.Value);
        var ward = address.Ward.ToString(CultureInfo.InvariantCulture);
        if (address.Plot.HasValue)
            return new(address.World, district, ward, address.Plot.Value.ToString(CultureInfo.InvariantCulture), IsApartment: false, IsSubdivision: false);
        if (address.Apartment.HasValue)
            return new(address.World, district, ward, address.Apartment.Value.ToString(CultureInfo.InvariantCulture), IsApartment: true, IsSubdivision: address.Subdivision == true);
        return null;
    }
}
