using System;
using System.Linq;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Listings;

/// <summary>
/// Records venue and event adverts shouted in chat (<see cref="Shouts"/>) on a world the character can
/// travel to in the listing cache, and saves the cache a few seconds after the last change.
/// </summary>
/// <remarks>
/// Shouts in a busy city come several a minute and a save snapshots every shout listing, so saves are
/// batched rather than made per shout.
/// </remarks>
internal sealed class ShoutListings : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly IPlayerState player;
    private readonly IPluginLog log;
    private readonly ListingCache cache;
    private readonly AddressParser parser;
    private readonly ShoutParts parts = new();
    private readonly Func<TravelRegion?> region;
    private readonly Action save;
    private DateTimeOffset? saveDue;

    public ShoutListings(
        IChatGui chatGui, IFramework framework, IPlayerState player, IPluginLog log, ListingCache cache, Gazetteer gazetteer, Func<TravelRegion?> region,
        Action save)
    {
        this.chatGui = chatGui;
        this.framework = framework;
        this.player = player;
        this.log = log;
        this.cache = cache;
        parser = new AddressParser(gazetteer);
        this.region = region;
        this.save = save;
        chatGui.ChatMessage += OnChatMessage;
        framework.Update += OnUpdate;
    }

    // Runs on the game thread, like every other use of the cache
    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (message.LogKind != XivChatType.Shout)
            return;

        try
        {
            // A shout reaches only the shouter's zone, so it was made on the world the character is on
            var world = player.IsLoaded && player.CurrentWorld.IsValid ? player.CurrentWorld.Value.Name.ExtractText() : null;
            if (region() is not { } travel || !travel.IncludesWorld(world))
                return;

            // The sender carries a player link with their home world, except on the character's own shouts
            var sender = message.OriginalSender.ToDalamudString();
            var id = sender.Payloads.OfType<PlayerPayload>().FirstOrDefault() is { } shouter
                ? Shouts.IdOf(shouter.PlayerName, shouter.World.ValueNullable?.Name.ExtractText())
                : Shouts.IdOf(sender.TextValue, null);
            var now = DateTimeOffset.UtcNow;
            // As text with the game's glyphs, like a Party Finder ad's, so the same parsers read it
            var text = parts.Join(parser, id, message.OriginalMessage.ToDalamudString().TextValue, world, now);
            if (Shouts.Record(cache, parser, id, text, world, now) is not null)
                saveDue ??= now + SaveDelay;
        }
        catch (Exception e)
        {
            log.Error(e, "Couldn't record a shout");
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
        chatGui.ChatMessage -= OnChatMessage;
        framework.Update -= OnUpdate;
        Flush();
    }
}
