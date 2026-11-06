namespace Jaunt.Core.Capture;

/// <summary>
/// One received Party Finder listing, as written to the dev capture file.
/// </summary>
/// <remarks>
/// Deliberately has no field for the host's name, content ID, home world or current world: the
/// corpus needs the ad, not who posted it. Adding a field here changes what is captured, so the
/// tests pin the exact set of fields.
/// </remarks>
/// <param name="CapturedAt">When the plugin received the listing.</param>
/// <param name="ListingId">The server's ID for the listing; repeats when the listing is seen again.</param>
/// <param name="BatchNumber">The batch the game delivered the listing in.</param>
/// <param name="WorldId">Row ID of the world the listing was created on.</param>
/// <param name="WorldName">Name of that world.</param>
/// <param name="Category">The Party Finder category, by name.</param>
/// <param name="SecondsRemaining">How long the listing stays up, as of <paramref name="CapturedAt"/>.</param>
/// <param name="Description">The description's text, without icons or other payloads.</param>
/// <param name="DescriptionRaw">The description's encoded SeString bytes, which keep icon characters and other payloads.</param>
public sealed record CapturedListing(
    DateTimeOffset CapturedAt,
    ulong ListingId,
    int BatchNumber,
    uint WorldId,
    string WorldName,
    string Category,
    ushort SecondsRemaining,
    string Description,
    byte[] DescriptionRaw);
