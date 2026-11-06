using System.Security.Cryptography;
using System.Text;

namespace Jaunt.Core.Capture;

/// <summary>
/// Turns sender names into short pseudonyms that match within one capture session only.
/// </summary>
/// <remarks>
/// HMAC-SHA256 with a random key that is never stored, truncated to 8 bytes. Lines from one sender
/// share a key in the same file, so repeats and multi-line ads can be grouped, but the key can't be
/// reversed by hashing candidate names or matched across sessions.
/// </remarks>
public sealed class SenderKeys
{
    private readonly byte[] secret;

    /// <summary>A new session with a random secret.</summary>
    public SenderKeys() : this(RandomNumberGenerator.GetBytes(32)) { }

    /// <param name="secret">The session's secret; tests pass a fixed one.</param>
    public SenderKeys(byte[] secret) => this.secret = secret;

    /// <summary>The sender's pseudonym: 16 lowercase hex digits.</summary>
    public string For(string sender)
    {
        var mac = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(sender));
        return Convert.ToHexStringLower(mac.AsSpan(0, 8));
    }
}
