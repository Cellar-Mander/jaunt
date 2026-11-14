using System;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;
using Jaunt.Listings;
using Jaunt.Text;
using Jaunt.Travel;
using Jaunt.Windows;

namespace Jaunt;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IPartyFinderGui PartyFinderGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private const string CommandName = "/jaunt";

    public Configuration Configuration { get; init; }

    /// <summary>Listings kept between sessions; use from the game thread and save with <see cref="SaveListingCache"/>.</summary>
    internal ListingCache ListingCache { get; }
    private readonly ListingCacheStore listingCacheStore;
    private readonly PartyFinderListings partyFinderListings;
    private readonly FfxivVenuesListings ffxivVenuesListings;

    public readonly WindowSystem WindowSystem = new("Jaunt");
    private ConfigWindow ConfigWindow { get; init; }
    private ListingsWindow ListingsWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Normalize();

        listingCacheStore = new ListingCacheStore(PluginInterface.ConfigDirectory.FullName, Log);
        ListingCache = listingCacheStore.Load();
        if (ListingCache.RemoveExpired(DateTimeOffset.UtcNow) > 0)
            SaveListingCache();

        // Commit hash included, to check which checkout's build the game has loaded
        Log.Information($"Jaunt {typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} loaded");

        partyFinderListings = new PartyFinderListings(PartyFinderGui, Framework, Log, ListingCache, SaveListingCache);

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        var gazetteer = GazetteerLoader.Load(DataManager);
        ffxivVenuesListings = new FfxivVenuesListings(ListingCache, gazetteer, Configuration, Framework, Log, SaveListingCache);
        ListingsWindow = new ListingsWindow(ListingCache, new AddressParser(gazetteer), new Lifestream(PluginInterface, Log), NotificationManager, ffxivVenuesListings.FetchIfDue);
        WindowSystem.AddWindow(ListingsWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the venue listings. /jaunt config → open the settings"
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        // Unhook from Dalamud first, so no command or draw call reaches a disposed component
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        partyFinderListings.Dispose();
        ffxivVenuesListings.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        listingCacheStore.Dispose();
    }

    internal void SaveListingCache() => listingCacheStore.Save(ListingCache);

    // "config" always opens the settings rather than toggling, so it never closes a window someone
    // asked to see.
    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
            ConfigWindow.IsOpen = true;
        else
            ToggleMainUi();
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    public void ToggleMainUi() => ListingsWindow.Toggle();
}
