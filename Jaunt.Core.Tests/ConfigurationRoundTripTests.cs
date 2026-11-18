using Jaunt.Core;
using Jaunt.Core.Browser;
using Jaunt.Core.Display;
using Jaunt.Core.Listings;
using Newtonsoft.Json;

namespace Jaunt.Core.Tests;

public class ConfigurationRoundTripTests
{
    // Dalamud's PluginConfigurations saves and loads plugin configs with these settings
    private static readonly JsonSerializerSettings DalamudSettings = new()
    {
        TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
        TypeNameHandling = TypeNameHandling.Objects,
    };

    // Stands in for the plugin's Configuration, which adds only Dalamud's interface
    private sealed class TestConfiguration : ConfigurationBase
    {
        public int Version { get; set; }
    }

    // As the plugin does on load: deserialize, then Normalize()
    private static ConfigurationBase SaveAndLoad(ConfigurationBase config)
    {
        var json = JsonConvert.SerializeObject(config, Formatting.Indented, DalamudSettings);
        return Load(json);
    }

    private static ConfigurationBase Load(string json)
    {
        var loaded = JsonConvert.DeserializeObject<ConfigurationBase>(json, DalamudSettings)!;
        loaded.Normalize();
        return loaded;
    }

    [Fact]
    public void SavedValuesLoadBack()
    {
        var config = new TestConfiguration
        {
            RefreshCooldownMinutes = 45,
            TimeFormat = TimeFormat.TwentyFourHour,
            DateFormat = DateFormat.Iso,
        };

        var loaded = SaveAndLoad(config);

        Assert.Equal(45, loaded.RefreshCooldownMinutes);
        Assert.Equal(TimeFormat.TwentyFourHour, loaded.TimeFormat);
        Assert.Equal(DateFormat.Iso, loaded.DateFormat);
    }

    [Fact]
    public void TheSelectedListingLoadsBack()
    {
        var loaded = SaveAndLoad(new TestConfiguration { SelectedSource = ListingSource.Partake, SelectedId = "42" });

        Assert.Equal(ListingSource.Partake, loaded.SelectedSource);
        Assert.Equal("42", loaded.SelectedId);
    }

    [Theory]
    [InlineData(99, "42")]
    [InlineData(3, null)]
    [InlineData(null, "42")]
    public void AnIncompleteOrUnknownSelectionIsCleared(int? source, string? id)
    {
        var loaded = SaveAndLoad(new TestConfiguration { SelectedSource = (ListingSource?)source, SelectedId = id });

        Assert.Null(loaded.SelectedSource);
        Assert.Null(loaded.SelectedId);
    }

    [Fact]
    public void DefaultsAreTenMinutesAndTheSystemFormats()
    {
        var config = new TestConfiguration();

        Assert.Equal(10, config.RefreshCooldownMinutes);
        Assert.Equal(TimeFormat.System, config.TimeFormat);
        Assert.Equal(DateFormat.System, config.DateFormat);
    }

    [Fact]
    public void ConfigSavedBeforeTheseSettingsLoadsDefaults()
    {
        var json = JsonConvert.SerializeObject(new TestConfiguration(), DalamudSettings)
            .Replace("\"RefreshCooldownMinutes\":10,", "", StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCooldownMinutes", json, StringComparison.Ordinal);

        Assert.Equal(RefreshPolicy.DefaultCooldownMinutes, Load(json).RefreshCooldownMinutes);
    }

    [Theory]
    [InlineData(-1, RefreshPolicy.MinCooldownMinutes)]
    [InlineData(9, RefreshPolicy.MinCooldownMinutes)]
    [InlineData(10, 10)]
    [InlineData(1440, 1440)]
    [InlineData(1441, RefreshPolicy.MaxCooldownMinutes)]
    public void HandEditedCooldownIsPulledIntoRange(int saved, int expected)
    {
        var config = new TestConfiguration { RefreshCooldownMinutes = saved };

        Assert.Equal(expected, SaveAndLoad(config).RefreshCooldownMinutes);
    }

    [Fact]
    public void HandEditedUnknownFormatsFallBackToSystem()
    {
        var config = new TestConfiguration { TimeFormat = (TimeFormat)7, DateFormat = (DateFormat)(-1) };

        var loaded = SaveAndLoad(config);

        Assert.Equal(TimeFormat.System, loaded.TimeFormat);
        Assert.Equal(DateFormat.System, loaded.DateFormat);
    }

    [Fact]
    public void EverySectionStartsExpanded()
    {
        var config = new TestConfiguration();
        config.Normalize();

        Assert.Equal([SettingsSection.Data, SettingsSection.Display, SettingsSection.Listings], config.ExpandedSections!.Order());
    }

    [Fact]
    public void CollapsedSectionsStayCollapsed()
    {
        var config = new TestConfiguration { ExpandedSections = [SettingsSection.Listings] };

        Assert.Equal([SettingsSection.Listings], SaveAndLoad(config).ExpandedSections!);
    }

    [Fact]
    public void AllSectionsCollapsedIsKept()
    {
        var config = new TestConfiguration { ExpandedSections = [] };

        Assert.Empty(SaveAndLoad(config).ExpandedSections!);
    }

    [Fact]
    public void HandEditedUnknownSectionsAreDropped()
    {
        var config = new TestConfiguration { ExpandedSections = [SettingsSection.Data, (SettingsSection)9] };

        Assert.Equal([SettingsSection.Data], SaveAndLoad(config).ExpandedSections!);
    }

    [Fact]
    public void InterestsKeepTheNumbersSavedFiltersUse()
    {
        // Saved filters store interests by number: a changed or reused bit would make a saved
        // filter pick a different interest. Add new interests here with a bit no one has used.
        (Interests Interest, int Bit)[] saved =
        [
            (Interests.Clubs, 1 << 0), (Interests.Bars, 1 << 1), (Interests.Cafes, 1 << 2),
            (Interests.LiveShows, 1 << 3), (Interests.Games, 1 << 4), (Interests.Roleplay, 1 << 5),
            (Interests.Spa, 1 << 6), (Interests.Art, 1 << 7), (Interests.Photography, 1 << 8),
            (Interests.Tarot, 1 << 9), (Interests.Chill, 1 << 10), (Interests.Gambling, 1 << 11),
        ];

        Assert.All(saved, s => Assert.Equal(s.Bit, (int)s.Interest));
        Assert.Equal(
            Enum.GetValues<Interests>().Where(i => i != Interests.None).Order(),
            saved.Select(s => s.Interest).Order());
    }

    [Fact]
    public void FiltersLoadBack()
    {
        var config = new TestConfiguration
        {
            Filters = new BrowserFilters
            {
                SfwOnly = false, Favourites = true, HideVisited = true, ShowHidden = true,
                Tags = Interests.Bars | Interests.LiveShows, Search = "jazz -\"open mic\"",
            },
        };

        var loaded = SaveAndLoad(config).Filters;

        Assert.False(loaded.SfwOnly);
        Assert.True(loaded.Favourites);
        Assert.True(loaded.HideVisited);
        Assert.True(loaded.ShowHidden);
        Assert.Equal(Interests.Bars | Interests.LiveShows, loaded.Tags);
        Assert.Equal("jazz -\"open mic\"", loaded.Search);
    }

    [Fact]
    public void ConfigSavedBeforeFiltersLoadsSfwOnlyAndNothingElse()
    {
        var json = JsonConvert.SerializeObject(new TestConfiguration(), DalamudSettings);
        var start = json.IndexOf("\"Filters\":", StringComparison.Ordinal);
        var end = json.IndexOf('}', start) + 2;
        json = json.Remove(start, end - start);
        Assert.DoesNotContain("Filters", json, StringComparison.Ordinal);

        var filters = Load(json).Filters;

        Assert.True(filters.SfwOnly);
        Assert.False(filters.Favourites || filters.HideVisited || filters.ShowHidden);
        Assert.Equal(Interests.None, filters.Tags);
        Assert.Equal("", filters.Search);
    }

    [Fact]
    public void HandEditedNullFiltersAndUnknownTagsAreCleaned()
    {
        var nulled = SaveAndLoad(new TestConfiguration { Filters = null! });
        Assert.True(nulled.Filters.SfwOnly);

        var odd = SaveAndLoad(new TestConfiguration { Filters = new BrowserFilters { Tags = Interests.Bars | (Interests)(1 << 20), Search = null! } });
        Assert.Equal(Interests.Bars, odd.Filters.Tags);
        Assert.Equal("", odd.Filters.Search);
    }

    [Fact]
    public void KnownFormatsAreKept()
    {
        var config = new TestConfiguration { TimeFormat = TimeFormat.TwelveHour, DateFormat = DateFormat.MonthDay };

        var loaded = SaveAndLoad(config);

        Assert.Equal(TimeFormat.TwelveHour, loaded.TimeFormat);
        Assert.Equal(DateFormat.MonthDay, loaded.DateFormat);
    }
}
