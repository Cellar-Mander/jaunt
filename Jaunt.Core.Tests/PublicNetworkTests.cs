using System.Net;
using Jaunt.Core.Images;

namespace Jaunt.Core.Tests;

public class PublicNetworkTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("104.21.3.4", true)]
    [InlineData("172.15.0.1", true)]
    [InlineData("172.32.0.1", true)]
    [InlineData("100.63.0.1", true)]
    [InlineData("100.128.0.1", true)]
    [InlineData("169.253.0.1", true)]
    [InlineData("192.169.0.1", true)]
    [InlineData("198.20.0.1", true)]
    [InlineData("2606:4700::6810:84e5", true)]
    [InlineData("192.88.98.1", true)]
    [InlineData("192.0.1.1", true)]
    [InlineData("198.51.99.1", true)]
    [InlineData("198.52.100.1", true)]
    [InlineData("203.0.114.1", true)]
    [InlineData("203.1.113.1", true)]
    [InlineData("2001:db9::1", true)]
    [InlineData("2001:cb8::1", true)]
    [InlineData("2001:1db8::1", true)]
    [InlineData("2101:db8::1", true)]
    [InlineData("192.88.99.1", false)]
    [InlineData("1.0.0.1", true)]
    [InlineData("9.255.255.255", true)]
    [InlineData("11.0.0.0", true)]
    [InlineData("126.255.255.255", true)]
    [InlineData("128.0.0.0", true)]
    [InlineData("192.167.255.255", true)]
    [InlineData("198.17.255.255", true)]
    [InlineData("198.20.0.0", true)]
    [InlineData("223.255.255.255", true)]
    [InlineData("::2", true)]
    [InlineData("fbff::1", true)]
    [InlineData("fe00::1", true)]
    [InlineData("feff::1", false)]
    [InlineData("fe7f::1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.255.255.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("100.127.255.255", false)]
    [InlineData("169.254.169.254", false)] // cloud metadata
    [InlineData("192.0.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("198.51.100.1", false)]
    [InlineData("203.0.113.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("fe80::1", false)]
    [InlineData("fec0::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("2001:db8::1", false)]
    public void OnlyPublicInternetAddressesAreConnectedTo(string address, bool expected)
    {
        Assert.Equal(expected, PublicNetwork.IsPublic(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:9/")]
    [InlineData("http://localhost:9/")]
    [InlineData("http://192.168.0.1:9/")]
    public async Task RequestsToPrivateAddressesFailBeforeConnecting(string url)
    {
        using var client = new HttpClient(PublicNetwork.Handler());

        var error = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            using var response = await client.GetAsync(url);
        });

        Assert.Equal(HttpRequestError.ConnectionError, error.HttpRequestError);
        Assert.Contains("not a public internet address", error.InnerException?.Message ?? error.Message, StringComparison.Ordinal);
    }
}
