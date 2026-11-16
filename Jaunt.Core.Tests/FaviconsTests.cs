using Jaunt.Core.Images;

namespace Jaunt.Core.Tests;

public class FaviconsTests
{
    private static readonly Uri Page = new("https://lounge.example/home/");

    private static string[] Candidates(string? html) => [.. Favicons.Candidates(html, Page).Select(u => u.AbsoluteUri)];

    [Fact]
    public void AppleTouchIconsComeFirstThenIconsByLargestSizeThenFaviconIco()
    {
        var html = """
            <head>
            <link rel="icon" href="/small.png" sizes="16x16">
            <LINK REL='shortcut icon' HREF='big.png' SIZES='16x16 64x64'>
            <link rel=apple-touch-icon-precomposed href=/touch.png>
            """;

        Assert.Equal(["https://lounge.example/touch.png", "https://lounge.example/home/big.png", "https://lounge.example/small.png"], Candidates(html));
    }

    [Fact]
    public void FaviconIcoIsTriedLastWhenThereIsRoom()
    {
        Assert.Equal(
            ["https://lounge.example/a.png", "https://lounge.example/favicon.ico"],
            Candidates("""<link rel="icon" href="https://lounge.example/a.png">"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html><head><title>No icons</title><link rel=\"stylesheet\" href=\"/a.css\"></head>")]
    [InlineData("<link rel=\"icon\" href=\"/i.svg\">")]
    [InlineData("<link rel=\"icon\" type=\"image/svg+xml\" href=\"/i\">")]
    [InlineData("<link rel=\"icon\" href=\"javascript:alert(1)\">")]
    [InlineData("<link rel=\"icon\">")]
    [InlineData("<link href=\"/i.png\">")]
    [InlineData("<link rel=\"icon\" href=\"http://[bad\">")]
    [InlineData("<linkage rel=\"icon\" href=\"/i.png\">")]
    [InlineData("<link rel=\"icon\" href=\"/i.png\"")] // unclosed at the end of what was read
    public void WithoutAUsableIconLinkOnlyFaviconIcoIsTried(string? html)
    {
        var expected = html?.EndsWith("\"/i.png\"", StringComparison.Ordinal) == true
            ? new[] { "https://lounge.example/i.png", "https://lounge.example/favicon.ico" }
            : ["https://lounge.example/favicon.ico"];

        Assert.Equal(expected, Candidates(html));
    }

    [Fact]
    public void AddressesAreDecodedAndKeptOnceAndAtMostThreeTried()
    {
        var html = """
            <link rel="icon" href="/a.png?v=1&amp;w=2"><link rel="icon" href="/a.png?v=1&w=2">
            <link rel="icon" href="/b.png"><link rel="icon" href="/c.png"><link rel="icon" href="/d.png">
            """;

        Assert.Equal(["https://lounge.example/a.png?v=1&w=2", "https://lounge.example/b.png", "https://lounge.example/c.png"], Candidates(html));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("any", 0)]
    [InlineData("32x32", 32)]
    [InlineData("16x16 180X180 64x64", 180)]
    [InlineData("x32 axb -5x5", 0)]
    public void LargestIsTheWidestSizeGiven(string? sizes, int expected)
    {
        Assert.Equal(expected, Favicons.Largest(sizes));
    }

    [Theory]
    [InlineData("<link")]
    [InlineData("<link ")]
    [InlineData("<link rel")]
    [InlineData("<link rel ")]
    [InlineData("<link rel=")]
    [InlineData("<link rel= ")]
    [InlineData("<link rel=\"icon")]
    [InlineData("<link rel=icon")]
    [InlineData("<link rel=icon/")]
    [InlineData("<link rel=icon /")]
    public void APageCutOffInsideATagIsReadWithoutFailing(string html)
    {
        Assert.Equal(["https://lounge.example/favicon.ico"], Candidates(html));
    }

    [Fact]
    public void ASlashBetweenAttributesIsSkipped()
    {
        var tag = Assert.Single(Favicons.LinkTags("<link / rel=\"icon\" / href=\"/a.png\">"));

        Assert.Equal(("icon", "/a.png"), (tag["rel"], tag["href"]));
    }

    [Fact]
    public void AQuotedValueMayHoldAGreaterThanSign()
    {
        var tag = Assert.Single(Favicons.LinkTags("<link href=\"/a.png?x=>\" rel=\"icon\">"));

        Assert.Equal(("icon", "/a.png?x=>"), (tag["rel"], tag["href"]));
    }

    [Fact]
    public void LinkTagsReadQuotedBareAndValuelessAttributes()
    {
        var tag = Assert.Single(Favicons.LinkTags("<link data-x rel = \"icon\" href='/a b.png' sizes=32x32/>"));

        Assert.Equal("icon", tag["rel"]);
        Assert.Equal("/a b.png", tag["href"]);
        Assert.Equal("32x32", tag["sizes"]);
        Assert.Equal("", tag["data-x"]);
    }
}
