using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Jaunt.Core;

namespace Jaunt.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Jaunt###JauntConfig")
    {
        Flags = ImGuiWindowFlags.AlwaysAutoResize;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var value = configuration.ExampleValue;
        if (ImGui.SliderInt("Example value", ref value, ConfigurationBase.MinExampleValue, ConfigurationBase.MaxExampleValue))
        {
            configuration.ExampleValue = value;
            configuration.Save();
        }
    }
}
