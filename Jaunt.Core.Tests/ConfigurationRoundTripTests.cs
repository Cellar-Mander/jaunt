using Jaunt.Core;
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
        var loaded = JsonConvert.DeserializeObject<ConfigurationBase>(json, DalamudSettings)!;
        loaded.Normalize();
        return loaded;
    }

    [Fact]
    public void SavedValueLoadsBack()
    {
        var config = new TestConfiguration { ExampleValue = 7 };

        Assert.Equal(7, SaveAndLoad(config).ExampleValue);
    }

    [Theory]
    [InlineData(-1, ConfigurationBase.MinExampleValue)]
    [InlineData(101, ConfigurationBase.MaxExampleValue)]
    [InlineData(ConfigurationBase.MinExampleValue, ConfigurationBase.MinExampleValue)]
    [InlineData(ConfigurationBase.MaxExampleValue, ConfigurationBase.MaxExampleValue)]
    public void HandEditedValueIsPulledIntoRange(int saved, int expected)
    {
        var config = new TestConfiguration { ExampleValue = saved };

        Assert.Equal(expected, SaveAndLoad(config).ExampleValue);
    }
}
