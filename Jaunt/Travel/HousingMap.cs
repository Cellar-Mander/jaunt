using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using Jaunt.Core.Text;
using Jaunt.Text;
using Lumina.Excel.Sheets;

namespace Jaunt.Travel;

/// <summary>A place on a game map: its zone, the map it is drawn on, and its position in the world.</summary>
internal readonly record struct MapSpot(uint Territory, uint Map, Vector3 Position);

/// <summary>
/// Where each housing plot and apartment building is, read once from the game's
/// <c>HousingMapMarkerInfo</c> (a row per district's zone, a subrow per <see cref="HousingMarker"/>),
/// and opens the game map there with a flag on it.
/// </summary>
internal sealed class HousingMap(Dictionary<District, MapSpot[]> spots, IGameGui gameGui)
{
    public static HousingMap Load(IDataManager data, IGameGui gameGui)
    {
        var markers = data.GetSubrowExcelSheet<HousingMapMarkerInfo>();
        var spots = new Dictionary<District, MapSpot[]>();
        foreach (var (territory, district) in GazetteerLoader.Districts)
        {
            // A district whose markers the game data lacks, or has fewer of, gets no Map buttons
            if (markers.GetRowOrDefault(territory) is not { Count: > HousingMarker.SubdivisionApartments } row)
                continue;
            var list = new MapSpot[row.Count];
            for (var i = 0; i < row.Count; i++)
            {
                var marker = row[i];
                list[i] = new MapSpot(territory, marker.Map.RowId, new Vector3(marker.X, marker.Y, marker.Z));
            }

            spots[district] = list;
        }

        return new HousingMap(spots, gameGui);
    }

    /// <summary>The plot or apartment building the address names, or null when it names none the game data has.</summary>
    public MapSpot? SpotOf(HousingAddress address) =>
        HousingMarker.Index(address) is { } index && spots.TryGetValue(address.District!.Value, out var list) && index < list.Length
            ? list[index]
            : null;

    /// <summary>Opens the game map on the spot, flagged; false if the game couldn't.</summary>
    public bool Show(MapSpot spot) => gameGui.OpenMapWithMapLink(spot.Territory, spot.Map, spot.Position);
}
