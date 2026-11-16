using System.Text.Json;

namespace Jaunt.Core.Images;

/// <summary>Reads the images of the server behind a Discord invite from Discord's public invite API.</summary>
public static class DiscordInvites
{
    /// <summary>The invite API; needs no sign-in for an invite's server name and images.</summary>
    /// <param name="code">The invite code, checked with <see cref="ImageSources.IsInviteCode"/>.</param>
    public static Uri Url(string code) => new($"https://discord.com/api/v10/invites/{Uri.EscapeDataString(code)}");

    /// <summary>A server's images on Discord's CDN, each null when it has none.</summary>
    public sealed record Images(Uri? Icon, Uri? Banner, Uri? Splash);

    /// <summary>
    /// Reads the invite API's response: the server's icon at 128 px, and its banner and invite splash
    /// at 1024 px, all as PNG. Null for an invite to a group DM or a response without a server.
    /// </summary>
    /// <exception cref="JsonException">The text isn't JSON.</exception>
    public static Images? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("guild", out var guild) || guild.ValueKind != JsonValueKind.Object
            || Text(guild, "id") is not { } id || !id.All(char.IsAsciiDigit))
            return null;

        return new Images(
            Cdn("icons", id, Text(guild, "icon"), 128),
            Cdn("banners", id, Text(guild, "banner"), 1024),
            Cdn("splashes", id, Text(guild, "splash"), 1024));
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // Animated images' hashes start "a_"; asking for .png gives their first frame. Hashes are 32 hex
    // digits (34 when animated); the cap only keeps a hostile response's text out of the address.
    // Stryker disable once Equality : no real hash is near 64 characters
    private static Uri? Cdn(string folder, string id, string? hash, int size) =>
        hash is { Length: > 0 and <= 64 } && hash.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? new Uri($"https://cdn.discordapp.com/{folder}/{id}/{hash}.png?size={size}")
            : null;
}
