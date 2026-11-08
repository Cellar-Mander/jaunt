#if DEBUG
using System;
using System.IO;
using System.Text;
using Dalamud.Game.Gui.PartyFinder.Types;
using Dalamud.Plugin.Services;
using Jaunt.Core.Capture;

namespace Jaunt.Capture;

/// <summary>
/// Dev builds only: writes every Party Finder listing in the None category the game receives to
/// <c>pf-capture/listings-&lt;start time&gt;.jsonl</c> in the plugin's config folder, for the venue corpus.
/// </summary>
internal sealed class PartyFinderCapture : IDisposable
{
    private readonly IPartyFinderGui partyFinderGui;
    private readonly IPluginLog log;
    private readonly ListingRecorder recorder;

    public PartyFinderCapture(IPartyFinderGui partyFinderGui, IPluginLog log, string configDirectory)
    {
        this.partyFinderGui = partyFinderGui;
        this.log = log;

        var directory = Path.Combine(configDirectory, "pf-capture");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"listings-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        recorder = new ListingRecorder(new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));

        partyFinderGui.ReceiveListing += OnReceiveListing;
        log.Information($"Recording Party Finder listings to {path}");
    }

    // Runs on the game thread: copy the fields out and queue them; the recorder writes elsewhere
    private void OnReceiveListing(IPartyFinderListing listing, IPartyFinderListingEventArgs args)
    {
        // Venues and events advertise under None; the duty categories are noise for the corpus
        if (listing.Category != DutyCategory.None)
            return;

        try
        {
            recorder.Record(new CapturedListing(
                CapturedAt: DateTimeOffset.Now,
                ListingId: listing.Id,
                BatchNumber: args.BatchNumber,
                WorldId: listing.World.RowId,
                WorldName: listing.World.ValueNullable?.Name.ExtractText() ?? "",
                Category: listing.Category.ToString(),
                SecondsRemaining: listing.SecondsRemaining,
                Description: listing.Description.TextValue,
                DescriptionRaw: listing.Description.Encode()));
        }
        catch (Exception e)
        {
            log.Error(e, $"Couldn't record Party Finder listing {listing.Id}");
        }
    }

    public void Dispose()
    {
        partyFinderGui.ReceiveListing -= OnReceiveListing;

        try
        {
#pragma warning disable VSTHRD002 // Blocks unload briefly to write what's still queued; worth it for a dev-only capture
            recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
        catch (Exception e)
        {
            log.Error(e, "Party Finder capture failed; the file may be missing listings");
        }
    }
}
#endif
