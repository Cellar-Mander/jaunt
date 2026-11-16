using System;
using System.IO;
using System.Reflection;
using Dalamud.Game.Command;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Jaunt.Core.Images;
using Jaunt.Core.Listings;
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
    [PluginService] internal static ISelfTestRegistry SelfTestRegistry { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private const string CommandName = "/jaunt";

    public Configuration Configuration { get; init; }

    /// <summary>Listings kept between sessions; use from the game thread and save with <see cref="SaveListingCache"/>.</summary>
    internal ListingCache ListingCache { get; }
    private readonly ListingCacheStore listingCacheStore;
    private readonly PartyFinderListings partyFinderListings;
    private readonly FetchedListings xivpfListings;
    private readonly FetchedListings ffxivVenuesListings;
    private readonly FetchedListings partakeListings;
    private readonly FetchedListings rpEventCalendarListings;
    private readonly IFontHandle titleFontHandle;
    private readonly HeldFont titleFont;
    private readonly HeldFont iconFont;
    private readonly ImageStore imageStore;

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

        var gazetteer = GazetteerLoader.Load(DataManager);
        var region = new CurrentRegion(PlayerState, gazetteer);
        partyFinderListings = new PartyFinderListings(PartyFinderGui, Framework, Log, ListingCache, region.Get, SaveListingCache);

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        xivpfListings = FetchedListings.Xivpf(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        ffxivVenuesListings = FetchedListings.FfxivVenues(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        partakeListings = FetchedListings.Partake(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        rpEventCalendarListings = FetchedListings.RpEventCalendar(region.Get, ListingCache, Configuration, Framework, Log, SaveListingCache);
        // Titles are a quarter larger than the default font, at whatever size the user set it
        titleFontHandle = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(-1.25f)));
        titleFont = new HeldFont(titleFontHandle);
        iconFont = new HeldFont(PluginInterface.UiBuilder.IconFontHandle);
        imageStore = new ImageStore(
            Path.Combine(PluginInterface.ConfigDirectory.FullName, "images"), PublicNetwork.Handler(), FetchedListings.UserAgent,
            (message, e) => Log.Warning(e, message));
        ListingsWindow = new ListingsWindow(
            ListingCache, gazetteer, new Lifestream(PluginInterface, Log), NotificationManager, Configuration, FetchSources, SaveListingCache, region.Get, () => ConfigWindow.IsOpen = true,
            titleFont, iconFont, imageStore, TextureProvider, PlaceholderArt.Backdrops(DataManager));
        WindowSystem.AddWindow(ListingsWindow);
        SelfTestRegistry.RegisterTestSteps(new SelfTests(ConfigWindow, ListingsWindow, Configuration, gazetteer).Steps());

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the listing browser. /jaunt config → open the settings"
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
        xivpfListings.Dispose();
        ffxivVenuesListings.Dispose();
        partakeListings.Dispose();
        rpEventCalendarListings.Dispose();

        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        titleFont.Dispose();
        iconFont.Dispose();
        titleFontHandle.Dispose();
        imageStore.Dispose();
        listingCacheStore.Dispose();
    }

    internal void SaveListingCache() => listingCacheStore.Save(ListingCache);

    private void FetchSources()
    {
        xivpfListings.FetchIfDue();
        ffxivVenuesListings.FetchIfDue();
        partakeListings.FetchIfDue();
        rpEventCalendarListings.FetchIfDue();
    }

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
