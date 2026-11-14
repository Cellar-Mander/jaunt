using System;
using System.Globalization;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Listings;

/// <summary>
/// Records every Party Finder ad in the None category the game sends (venues and events advertise
/// there) on a world the character can travel to in the listing cache, and saves the cache a few seconds after the last change.
/// </summary>
/// <remarks>
/// The game sends listings in batches while the player browses the Party Finder, and a save
/// snapshots every in-game ad, so saves are batched rather than made per listing.
/// </remarks>
internal sealed class PartyFinderListings : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly IPartyFinderGui partyFinderGui;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly ListingCache cache;
    private readonly Func<TravelRegion?> region;
    private readonly Action save;
    private DateTimeOffset? saveDue;

    public PartyFinderListings(IPartyFinderGui partyFinderGui, IFramework framework, IPluginLog log, ListingCache cache, Func<TravelRegion?> region, Action save)
    {
        this.partyFinderGui = partyFinderGui;
        this.framework = framework;
        this.log = log;
        this.cache = cache;
        this.region = region;
        this.save = save;
        partyFinderGui.ReceiveListing += OnReceiveListing;
        framework.Update += OnUpdate;
    }

    // Runs on the game thread, like every other use of the cache
    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        if (listing.Category != DutyCategory.None)
            return;

        try
        {
            // The game sends only the character's data centre, so this rarely drops one; an unknown region keeps all
            var world = listing.World.ValueNullable?.Name.ExtractText();
            if (region() is { } travel && !travel.IncludesWorld(world))
                return;

            var now = DateTimeOffset.UtcNow;
            cache.SeenAd(
                ListingSource.InGamePartyFinder,
                listing.Id.ToString(CultureInfo.InvariantCulture),
                listing.Description.TextValue,
                world,
                now);
            saveDue ??= now + SaveDelay;
        }
        catch (Exception e)
        {
            log.Error(e, $"Couldn't record Party Finder listing {listing.Id}");
        }
    }

    private void OnUpdate(IFramework _)
    {
        if (saveDue is { } due && DateTimeOffset.UtcNow >= due)
            Flush();
    }

    /// <summary>Saves now if anything is waiting to be saved.</summary>
    public void Flush()
    {
        if (saveDue is null)
            return;
        saveDue = null;
        save();
    }

    public void Dispose()
    {
        partyFinderGui.ReceiveListing -= OnReceiveListing;
        framework.Update -= OnUpdate;
        Flush();
    }
}
