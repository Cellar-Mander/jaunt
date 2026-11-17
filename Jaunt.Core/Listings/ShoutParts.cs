using Jaunt.Core.Browser;
using Jaunt.Core.Text;

namespace Jaunt.Core.Listings;

/// <summary>
/// Joins an advert a shouter splits over several shouts: a shout heard within <see cref="Window"/> of
/// the same shouter's last one is read together with it, in the order heard, so an address in one part
/// and the performers or times in the other make one advert. Kept in memory only; nothing is saved.
/// </summary>
/// <remarks>
/// Not joined: a shout repeating the last part's text exactly, and two parts whose addresses are
/// different places (each gives one, and none is the same as or in the ward of one in the other). One
/// entry per shouter heard within the window; older ones are dropped once there are many.
/// </remarks>
public sealed class ShoutParts
{
    /// <summary>How soon after a shouter's last shout a new one counts as its next part.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    // Past this many shouters, those not heard within the window are forgotten
    private const int PruneAt = 256;

    private readonly Dictionary<string, (string Joined, string LastPart, DateTimeOffset At)> last = new(StringComparer.Ordinal);

    /// <summary>
    /// The text to read for a shout: joined after the shouter's earlier parts when it is the next part,
    /// else the shout alone. Remembers it as the shouter's last shout either way.
    /// </summary>
    /// <param name="id">The shouter's ID (<see cref="Shouts.IdOf"/>).</param>
    /// <param name="heardOn">The world the shout was heard on, for addresses that name none.</param>
    public string Join(AddressParser parser, string id, string text, string? heardOn, DateTimeOffset now)
    {
        if (last.Count >= PruneAt)
        {
            foreach (var stale in last.Where(p => now - p.Value.At > Window).Select(p => p.Key).ToList())
                last.Remove(stale);
        }

        var joined = text;
        if (last.TryGetValue(id, out var earlier) && now >= earlier.At && now - earlier.At <= Window
            && !string.Equals(earlier.LastPart, text, StringComparison.Ordinal)
            && SamePlace(Places(parser, earlier.Joined, heardOn), Places(parser, text, heardOn)))
            joined = earlier.Joined + "\n" + text;

        last[id] = (joined, text, now);
        return joined;
    }

    private static List<AddressKey> Places(AddressParser parser, string text, string? heardOn) =>
        [.. parser.Parse(text, heardOn).Select(AddressKey.Of).OfType<AddressKey>()];

    // Parts with no address between them can't name different places
    private static bool SamePlace(List<AddressKey> a, List<AddressKey> b) =>
        a.Count == 0 || b.Count == 0 || a.Exists(x => b.Exists(y => x == y || x.InWard == y || y.InWard == x));
}
