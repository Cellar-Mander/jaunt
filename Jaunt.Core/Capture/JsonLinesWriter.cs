using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;

namespace Jaunt.Core.Capture;

/// <summary>
/// Appends records to a JSON Lines file, one object per line, on a background task.
/// </summary>
/// <remarks>
/// <see cref="TryWrite"/> is cheap and doesn't block, so it can be called from the game thread.
/// Not thread-safe: call <see cref="TryWrite"/> from one thread.
/// </remarks>
public sealed class JsonLinesWriter<T> : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Keep non-ASCII text (Japanese, accents) readable in the file. Control characters, line
        // breaks and the game's private-use icon characters are still escaped as \uXXXX, so each
        // record stays on one line.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Channel<T> queue =
        Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly TextWriter writer;
    private readonly Task writeLoop;

    /// <param name="writer">Where lines go; this writer disposes it.</param>
    public JsonLinesWriter(TextWriter writer)
    {
        this.writer = writer;
        writeLoop = Task.Run(WriteLoopAsync);
    }

    /// <summary>Formats one record as a single JSON line, without the line break.</summary>
    public static string ToJsonLine(T record) => JsonSerializer.Serialize(record, JsonOptions);

    /// <summary>Queues the record to be written.</summary>
    /// <returns>False once writing has failed or the writer is disposed.</returns>
    public bool TryWrite(T record) => queue.Writer.TryWrite(record);

    private async Task WriteLoopAsync()
    {
        var reader = queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var record))
                    await writer.WriteLineAsync(ToJsonLine(record)).ConfigureAwait(false);

                // Flush once per burst, so a crash loses at most the records still queued
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            // Stop accepting records, so a failed file (disk full, say) doesn't fill memory instead;
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
