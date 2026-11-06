using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;

namespace Jaunt.Core.Capture;

/// <summary>
/// Appends received listings to a JSON Lines file, one object per line, skipping repeats.
/// </summary>
/// <remarks>
/// <see cref="Record"/> is cheap and doesn't block, so it can be called from the game thread; the
/// writes happen on a background task. Not thread-safe: call <see cref="Record"/> from one thread.
/// </remarks>
public sealed class ListingRecorder : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep non-ASCII text (Japanese, accents) readable in the file. Control characters, line
        // breaks and the game's private-use icon characters are still escaped as \uXXXX, so each
        // listing stays on one line.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Repeats: the game resends every listing on each page and refresh. One entry per distinct
    // listing seen this session, so it grows with the number of listings, not with refreshes.
    private readonly HashSet<(ulong Id, string Description)> seen = [];
    private readonly Channel<CapturedListing> queue =
        Channel.CreateUnbounded<CapturedListing>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly TextWriter writer;
    private readonly Task writeLoop;

    /// <param name="writer">Where lines go; the recorder disposes it.</param>
    public ListingRecorder(TextWriter writer)
    {
        this.writer = writer;
        writeLoop = Task.Run(WriteLoopAsync);
    }

    /// <summary>Formats one listing as a single JSON line, without the line break.</summary>
    public static string ToJsonLine(CapturedListing listing) => JsonSerializer.Serialize(listing, JsonOptions);

    /// <summary>
    /// Queues the listing to be written, unless one with the same ID and description was already
    /// recorded. An edited description counts as new.
    /// </summary>
    /// <returns>Whether the listing was queued.</returns>
    public bool Record(CapturedListing listing)
    {
        if (!seen.Add((listing.ListingId, listing.Description)))
            return false;

        return queue.Writer.TryWrite(listing);
    }

    private async Task WriteLoopAsync()
    {
        var reader = queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var listing))
                    await writer.WriteLineAsync(ToJsonLine(listing)).ConfigureAwait(false);

                // Flush once per burst, so a crash loses at most the listings still queued
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            // Stop accepting listings, so a failed file (disk full, say) doesn't fill memory instead;
            // DisposeAsync rethrows the error
            queue.Writer.TryComplete(e);
            throw;
        }
    }

    /// <summary>Writes everything still queued, then closes the file.</summary>
    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        try
        {
            await writeLoop.ConfigureAwait(false);
        }
        finally
        {
            await writer.DisposeAsync().ConfigureAwait(false);
        }
    }
}
