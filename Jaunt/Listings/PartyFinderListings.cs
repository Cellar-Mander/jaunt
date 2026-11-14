using System;
using System.Globalization;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using Jaunt.Core.Listings;

namespace Jaunt.Listings;

/// <summary>
/// Records every Party Finder ad in the None category the game sends (venues and events advertise
/// there) in the listing cache, and saves the cache a few seconds after the last change.
/// </summary>
/// <remarks>
/// The game sends listings in batches while the player browses the Party Finder, and saving
/// serialises the whole cache, so saves are batched rather than made per listing.
/// </remarks>
internal sealed class PartyFinderListings : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly IPartyFinderGui partyFinderGui;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly ListingCache cache;
    private readonly Action save;
    private DateTimeOffset? saveDue;

    public PartyFinderListings(IPartyFinderGui partyFinderGui, IFramework framework, IPluginLog log, ListingCache cache, Action save)
    {
        this.partyFinderGui = partyFinderGui;
        this.framework = framework;
        this.log = log;
        this.cache = cache;
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
            var description = listing.Description.TextValue;
            var now = DateTimeOffset.UtcNow;
            var cached = cache.Seen(
                ListingSource.InGamePartyFinder,
                listing.Id.ToString(CultureInfo.InvariantCulture),
                ListingDetails.Title(description),
                now);
            cached.Description = description;
            cached.ListedOn = listing.World.ValueNullable?.Name.ExtractText();
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
