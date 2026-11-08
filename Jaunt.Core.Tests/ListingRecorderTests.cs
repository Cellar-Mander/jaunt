using System.Text.Json;
using Jaunt.Core.Capture;

namespace Jaunt.Core.Tests;

public class ListingRecorderTests
{
    private static CapturedListing Listing(ulong id = 1, string description = "Open tonight 9pm ST") => new(
        CapturedAt: new DateTimeOffset(2026, 11, 6, 21, 0, 0, TimeSpan.Zero),
        ListingId: id,
        BatchNumber: 3,
        WorldId: 404,
        WorldName: "Marilith",
        Category: "None",
        SecondsRemaining: 3600,
        Description: description,
        DescriptionRaw: [0x02, 0x48, 0x03]);

    private static async Task<string[]> RecordAll(params CapturedListing[] listings)
    {
        using var output = new StringWriter();
        var recorder = new ListingRecorder(output);
        foreach (var listing in listings)
            recorder.Record(listing);
        await recorder.DisposeAsync();
        return output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void LineHasExactlyTheAllowedFields()
    {
        // Pins what leaves the game: a new field must be added here on purpose. The host's name,
        // content ID and worlds must never appear.
        using var json = JsonDocument.Parse(ListingRecorder.ToJsonLine(Listing()));

        var names = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Equal(
            ["batchNumber", "capturedAt", "category", "description", "descriptionRaw", "listingId", "secondsRemaining", "worldId", "worldName"],
            names);
    }

    [Fact]
    public void LineRoundTripsValues()
    {
        var listing = Listing();
        using var json = JsonDocument.Parse(ListingRecorder.ToJsonLine(listing));
        var root = json.RootElement;

        Assert.Equal(listing.CapturedAt, root.GetProperty("capturedAt").GetDateTimeOffset());
        Assert.Equal(listing.ListingId, root.GetProperty("listingId").GetUInt64());
        Assert.Equal(listing.DescriptionRaw, root.GetProperty("descriptionRaw").GetBytesFromBase64());
        Assert.Equal(listing.WorldName, root.GetProperty("worldName").GetString());
    }

    [Fact]
    public void MultiLineDescriptionStaysOnOneLine()
    {
        var line = ListingRecorder.ToJsonLine(Listing(description: "Line one\nLine two\r\nLine three"));

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal("Line one\nLine two\r\nLine three", JsonDocument.Parse(line).RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public void IconAndNonAsciiTextSurvives()
    {
        // U+E03C is in the private-use range the game's icon characters use. The encoder escapes
        // those as , which still decodes to the same text; other non-ASCII stays readable.
        const string description = " Café 営業中";
        var line = ListingRecorder.ToJsonLine(Listing(description: description));

        Assert.Contains("Café 営業中", line);
        Assert.Equal(description, JsonDocument.Parse(line).RootElement.GetProperty("description").GetString());
    }

    [Fact]
    public async Task RepeatedListingIsWrittenOnce()
    {
        var lines = await RecordAll(Listing(id: 1), Listing(id: 2), Listing(id: 1));

        Assert.Equal([1UL, 2UL], lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("listingId").GetUInt64()));
    }

    [Fact]
    public async Task EditedDescriptionIsWrittenAgain()
    {
        var lines = await RecordAll(Listing(id: 1, description: "Open 9pm"), Listing(id: 1, description: "Open 10pm"));

        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task RecordReportsWhetherListingWasQueued()
    {
        await using var recorder = new ListingRecorder(new StringWriter());

        Assert.True(recorder.Record(Listing(id: 1)));
        Assert.False(recorder.Record(Listing(id: 1)));
    }

    [Fact]
    public async Task DisposeWritesEverythingQueuedAndClosesWriter()
    {
        // revert-check: not revert-sensitive (only changed to dispose its writer for IDISP001; no behaviour change to guard)
        using var output = new TrackingWriter();
        var recorder = new ListingRecorder(output);
        for (ulong id = 0; id < 1000; id++)
            recorder.Record(Listing(id: id));

        await recorder.DisposeAsync();

        Assert.Equal(1000, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.True(output.Disposed);
    }

    [Fact]
    public async Task WrittenListingsAreFlushedBeforeDispose()
    {
        // A crash or game exit skips Dispose, so lines must reach the file while still recording
        // revert-check: not revert-sensitive (only changed to dispose its writer for IDISP001; no behaviour change to guard)
        using var output = new TrackingWriter();
        await using var recorder = new ListingRecorder(output);

        recorder.Record(Listing(id: 1));

        Assert.True(await Eventually(() => output.FlushCount > 0));
    }

    [Fact]
    public async Task WriteFailureStopsRecordingBeforeDispose()
    {
        var recorder = new ListingRecorder(new FailingWriter());
        recorder.Record(Listing(id: 1));

        // New IDs each try, so a refusal can only come from the stopped queue, not the repeat check
        ulong id = 2;
        Assert.True(await Eventually(() => !recorder.Record(Listing(id: id++))));

        await Assert.ThrowsAsync<IOException>(async () => await recorder.DisposeAsync());
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(10);
        }
        return condition();
    }

    private sealed class TrackingWriter : StringWriter
    {
        private int flushCount;

        public bool Disposed { get; private set; }

        public int FlushCount => Volatile.Read(ref flushCount);

        public override Task FlushAsync()
        {
            Interlocked.Increment(ref flushCount);
            return base.FlushAsync();
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FailingWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value) => throw new IOException("disk full");
    }
}
