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
    // Repeats: the game resends every listing on each page and refresh. One entry per distinct
    // listing seen this session, so it grows with the number of listings, not with refreshes.
    private readonly HashSet<(ulong Id, string Description)> seen = [];
    private readonly JsonLinesWriter<CapturedListing> lines;

    /// <param name="writer">Where lines go; the recorder disposes it.</param>
    public ListingRecorder(TextWriter writer) => lines = new JsonLinesWriter<CapturedListing>(writer);

    /// <summary>Formats one listing as a single JSON line, without the line break.</summary>
    public static string ToJsonLine(CapturedListing listing) => JsonLinesWriter<CapturedListing>.ToJsonLine(listing);

    /// <summary>
    /// Queues the listing to be written, unless one with the same ID and description was already
    /// recorded. An edited description counts as new.
    /// </summary>
    /// <returns>Whether the listing was queued.</returns>
    public bool Record(CapturedListing listing)
    {
        if (!seen.Add((listing.ListingId, listing.Description)))
            return false;

        return lines.TryWrite(listing);
    }

    /// <summary>Writes everything still queued, then closes the file.</summary>
    public ValueTask DisposeAsync() => lines.DisposeAsync();
}
