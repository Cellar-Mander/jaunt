namespace Jaunt.Core.Capture;

/// <summary>
/// One shout or yell line heard in game, as written to the dev capture file.
/// </summary>
/// <remarks>
/// Deliberately has no field for the sender's name, content ID or home world: the spike needs the
/// ad, and only needs to know which lines came from the same sender (<see cref="SenderKey"/>).
/// The world and zone are where the line was heard, not who sent it. Adding a field here changes
/// what is captured, so the tests pin the exact set of fields.
/// </remarks>
/// <param name="CapturedAt">When the plugin received the line.</param>
/// <param name="Channel">The chat channel by name: <c>Shout</c> or <c>Yell</c>.</param>
/// <param name="SenderKey">A per-session pseudonym for the sender; see <see cref="SenderKeys"/>.</param>
/// <param name="WorldId">Row ID of the world the player was on when the line arrived.</param>
/// <param name="WorldName">Name of that world.</param>
/// <param name="TerritoryId">Row ID of the zone (TerritoryType) the player was in.</param>
/// <param name="TerritoryName">The zone's place name.</param>
/// <param name="Message">The message's text, without icons or other payloads.</param>
/// <param name="MessageRaw">The message's encoded SeString bytes, which keep icons, links and auto-translate payloads.</param>
public sealed record CapturedChatLine(
    DateTimeOffset CapturedAt,
    string Channel,
    string SenderKey,
    uint WorldId,
    string WorldName,
    uint TerritoryId,
    string TerritoryName,
    string Message,
    byte[] MessageRaw);
