namespace Jaunt.Core.Text;

/// <summary>
/// Which of a district's map markers (the game's <c>HousingMapMarkerInfo</c> subrows) stands for a
/// housing address: plots 1–60 are 0–59, the apartment building 60 and the subdivision's 61.
/// </summary>
public static class HousingMarker
{
    /// <summary>Plots in a ward, 30 in the main division and 30 in the subdivision.</summary>
    public const int Plots = 60;

    /// <summary>The main division's apartment building.</summary>
    public const int Apartments = Plots;

    /// <summary>The subdivision's apartment building.</summary>
    public const int SubdivisionApartments = Plots + 1;

    /// <summary>
    /// The address's marker, or null when it names no single building: no district, neither a plot
    /// nor an apartment, or a plot outside 1–60. A plot wins over an apartment, as for travel; an
    /// apartment is in the main building unless the ad says subdivision.
    /// </summary>
    public static int? Index(HousingAddress address)
    {
        if (address.District is null)
            return null;
        if (address.Plot is { } plot)
            return plot is >= 1 and <= Plots ? plot - 1 : null;
        if (address.Apartment is not null)
            return address.Subdivision == true ? SubdivisionApartments : Apartments;
        return null;
    }
}
