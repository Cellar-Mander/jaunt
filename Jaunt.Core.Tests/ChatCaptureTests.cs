using System.Text.Json;
using Jaunt.Core.Capture;

namespace Jaunt.Core.Tests;

public class ChatCaptureTests
{
    private static CapturedChatLine Line(string message = "Open now! Mist W12 P30") => new(
        CapturedAt: new DateTimeOffset(2026, 11, 7, 21, 0, 0, TimeSpan.Zero),
        Channel: "Shout",
        SenderKey: "0123456789abcdef",
        WorldId: 404,
        WorldName: "Marilith",
        TerritoryId: 129,
        TerritoryName: "Limsa Lominsa Lower Decks",
        Message: message,
        MessageRaw: [0x02, 0x48, 0x03]);

    [Fact]
    public void LineHasExactlyTheAllowedFields()
    {
        // Pins what leaves the game: a new field must be added here on purpose. The sender's name,
        // content ID and home world must never appear.
        // revert-check: not revert-sensitive (pins the field set against future additions)
        using var json = JsonDocument.Parse(JsonLinesWriter<CapturedChatLine>.ToJsonLine(Line()));

        var names = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Equal(
            ["capturedAt", "channel", "message", "messageRaw", "senderKey", "territoryId", "territoryName", "worldId", "worldName"],
            names);
    }

    [Fact]
    public async Task RepeatedLinesAreAllWritten()
    {
        // Unlike PF listings, how often an ad is shouted is part of what the spike measures
        var output = new StringWriter();
        var writer = new JsonLinesWriter<CapturedChatLine>(output);
        Assert.True(writer.TryWrite(Line()));
        Assert.True(writer.TryWrite(Line()));
        await writer.DisposeAsync();

        Assert.Equal(2, output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void SenderKeyIsStableWithinASession()
    {
        var keys = new SenderKeys();

        Assert.Equal(keys.For("Alpha Beta"), keys.For("Alpha Beta"));
    }

    [Fact]
    public void DifferentSendersGetDifferentKeys()
    {
        var keys = new SenderKeys();

        Assert.NotEqual(keys.For("Alpha Beta"), keys.For("Gamma Delta"));
    }

    [Fact]
    public void SameSenderGetsDifferentKeysInDifferentSessions()
    {
        Assert.NotEqual(new SenderKeys().For("Alpha Beta"), new SenderKeys().For("Alpha Beta"));
    }

    [Fact]
    public void SenderKeyIsSixteenHexDigitsOfTheMac()
    {
        // HMAC-SHA256(key = 32 zero bytes, "Alpha Beta"), first 8 bytes; pins the algorithm and truncation
        var secret = new byte[32];
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData(secret, "Alpha Beta"u8.ToArray()).AsSpan(0, 8));

        var key = new SenderKeys(secret).For("Alpha Beta");

        Assert.Equal(expected, key);
        Assert.Matches("^[0-9a-f]{16}$", key);
    }
}
