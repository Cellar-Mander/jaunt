using System.Collections.Generic;
using Dalamud.Game;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Text;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace Jaunt.Windows;

/// <summary>
/// The pictograms and game art drawn for entries without an icon or splash image. See "Placeholders" under
/// "Icons and splash images" in <c>docs/design.md</c>.
/// </summary>
internal static class PlaceholderArt
{
    /// <summary>The pictogram for an interest <see cref="Placeholders.IconInterest"/> picked; a house for none.</summary>
    public static FontAwesomeIcon Icon(Interests interest) => interest switch
    {
        Interests.Clubs => FontAwesomeIcon.Headphones,
        Interests.Bars => FontAwesomeIcon.GlassMartiniAlt,
        Interests.Cafes => FontAwesomeIcon.MugHot,
        Interests.Spa => FontAwesomeIcon.Spa,
        Interests.Gambling => FontAwesomeIcon.Dice,
        Interests.LiveShows => FontAwesomeIcon.Music,
        Interests.Games => FontAwesomeIcon.Trophy,
        Interests.Art => FontAwesomeIcon.Palette,
        Interests.Photography => FontAwesomeIcon.Camera,
        Interests.Tarot => FontAwesomeIcon.Moon,
        Interests.Roleplay => FontAwesomeIcon.TheaterMasks,
        Interests.Chill => FontAwesomeIcon.Couch,
        _ => FontAwesomeIcon.Home,
    };

    // TerritoryIntendedUse rows for towns and field zones, the places written as an event's location
    private const uint Town = 0;
    private const uint Field = 1;

    // The LoadingImage row of the game's default loading screen, the world map, shown for zones with none of their own
    private const uint WorldMap = 1;

    /// <summary>
    /// The loading screens, as game texture paths, of each housing district (the art of the city it lies by),
    /// of each town and field zone by its English name, and the world map. Places whose image the game data
    /// lacks are left out.
    /// </summary>
    public static Backdrops Backdrops(IDataManager data)
    {
        var territories = data.GetExcelSheet<TerritoryType>();
        var names = data.GetExcelSheet<PlaceName>(ClientLanguage.English);

        string? PathOf(RowRef<LoadingImage> image)
        {
            if (!image.IsValid)
                return null;
            // The 1920×1080 version, not the _hr1 one at 3840×2160: the band is never that wide
            var path = $"ui/loadingimage/{image.Value.FileName.ExtractText()}.tex";
            return data.FileExists(path) ? path : null;
        }

        var districts = new Dictionary<District, string>();
        foreach (var (territory, district) in GazetteerLoader.Districts)
        {
            if (territories.GetRowOrDefault(territory) is { } row && PathOf(row.LoadingImage) is { } path)
                districts[district] = path;
        }

        var zones = new List<(string Name, string Art)>();
        foreach (var territory in territories)
        {
            if (territory.TerritoryIntendedUse.RowId is not (Town or Field))
                continue;
            var name = names.GetRowOrDefault(territory.PlaceName.RowId)?.Name.ExtractText();
            if (!string.IsNullOrEmpty(name) && PathOf(territory.LoadingImage) is { } path)
                zones.Add((name, path));
        }

        return new Backdrops(districts, zones, PathOf(new RowRef<LoadingImage>(data.Excel, WorldMap)));
    }
}
