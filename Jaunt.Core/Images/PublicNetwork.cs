using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Jaunt.Core.Images;

/// <summary>
/// An HTTP handler that connects only to public internet addresses. Image and favicon addresses come
/// from other players' ads, so without it an ad could make Jaunt send requests to the player's own
/// computer or home network (<c>http://192.168.1.1/</c>, or a name resolving to one).
/// </summary>
public static class PublicNetwork
{
    /// <summary>
    /// A handler whose every connection, redirects included, goes only to a public address. It uses
    /// no proxy, as the check is on the address connected to.
    /// </summary>
    public static SocketsHttpHandler Handler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        // Stryker disable once Boolean : tests run without a proxy, so using one or not looks the same
        UseProxy = false,
        MaxAutomaticRedirections = 5,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = ConnectAsync,
    };

    /// <summary>
    /// Whether an address is on the public internet: not loopback, private (10/8, 172.16/12,
    /// 192.168/16, fc00::/7), shared (100.64/10), link-local, multicast, unspecified or reserved.
    /// An IPv4 address mapped into IPv6 is judged as IPv4.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        foreach (var range in NotPublic)
        {
            // An IPv4 range never contains an IPv6 address, nor the other way round
            if (range.Contains(address))
                return false;
        }

        return true;
    }

    /// <summary>The special-purpose ranges (IANA's registries), none of them on the public internet.</summary>
    private static readonly IPNetwork[] NotPublic =
    [
        IPNetwork.Parse("0.0.0.0/8"), // this network
        IPNetwork.Parse("10.0.0.0/8"), // private
        IPNetwork.Parse("100.64.0.0/10"), // shared, carrier-grade NAT
        IPNetwork.Parse("127.0.0.0/8"), // loopback
        IPNetwork.Parse("169.254.0.0/16"), // link-local, cloud metadata
        IPNetwork.Parse("172.16.0.0/12"), // private
        IPNetwork.Parse("192.0.0.0/24"), // protocol assignments
        IPNetwork.Parse("192.0.2.0/24"), // documentation
        IPNetwork.Parse("192.88.99.0/24"), // 6to4 relay
        IPNetwork.Parse("192.168.0.0/16"), // private
        IPNetwork.Parse("198.18.0.0/15"), // benchmarking
        IPNetwork.Parse("198.51.100.0/24"), // documentation
        IPNetwork.Parse("203.0.113.0/24"), // documentation
        IPNetwork.Parse("224.0.0.0/3"), // multicast, reserved and broadcast
        IPNetwork.Parse("::/127"), // unspecified and loopback
        IPNetwork.Parse("fc00::/7"), // unique local
        IPNetwork.Parse("fe80::/10"), // link-local
        IPNetwork.Parse("fec0::/10"), // site-local, deprecated
        IPNetwork.Parse("ff00::/8"), // multicast
        IPNetwork.Parse("2001:db8::/32"), // documentation
    ];

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
        var allowed = addresses.Where(IsPublic).ToArray();
        if (allowed.Length == 0)
            // Stryker disable once String : the message is for the developer reading the log, not behaviour
            throw new HttpRequestException($"{host} is not a public internet address");

        // Connecting needs a reachable public host, which the tests don't rely on; RequestsToPrivateAddressesFailBeforeConnecting covers the refusal above
        // Stryker disable all
#pragma warning disable IDISP001 // The returned stream owns the socket; it is disposed here only if connecting fails
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
#pragma warning restore IDISP001
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        // Stryker restore all
    }
}
