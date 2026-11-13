using Jaunt.Core;
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
    public void KnownFormatsAreKept()
    {
        var config = new TestConfiguration { TimeFormat = TimeFormat.TwelveHour, DateFormat = DateFormat.MonthDay };

        var loaded = SaveAndLoad(config);

        Assert.Equal(TimeFormat.TwelveHour, loaded.TimeFormat);
        Assert.Equal(DateFormat.MonthDay, loaded.DateFormat);
    }
}
