using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Jaunt.Windows;

namespace Jaunt;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
#if DEBUG
    [PluginService] internal static IPartyFinderGui PartyFinderGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;

    private readonly Capture.PartyFinderCapture partyFinderCapture;
    private readonly Capture.ShoutYellCapture shoutYellCapture;
#endif

    private const string CommandName = "/jaunt";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("Jaunt");
    private ConfigWindow ConfigWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();

        // Commit hash included, to check which checkout's build the game has loaded
        Log.Information($"Jaunt {typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} loaded");

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Jaunt settings"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // While settings is the only window, hook only OpenConfigUi, so the installer shows just its
        // settings button. Add OpenMainUi when the plugin gets a main window.
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

#if DEBUG
        partyFinderCapture = new Capture.PartyFinderCapture(PartyFinderGui, Log, PluginInterface.ConfigDirectory.FullName);
        shoutYellCapture = new Capture.ShoutYellCapture(ChatGui, ClientState, PlayerState, DataManager, Log, PluginInterface.ConfigDirectory.FullName);
#endif
    }

    public void Dispose()
    {
        // Unhook from Dalamud first, so no command or draw call reaches a disposed component
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
#if DEBUG
        partyFinderCapture.Dispose();
        shoutYellCapture.Dispose();
#endif

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
    }

    private void OnCommand(string command, string args) => ToggleConfigUi();

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
