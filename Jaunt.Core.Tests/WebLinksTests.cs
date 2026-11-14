using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class WebLinksTests
{
    private static string[] Texts(string ad) => [.. WebLinks.Find(ad).Select(l => l.Text)];

    [Theory]
    [InlineData("Join https://discord.gg/abc now", "https://discord.gg/abc", "https://discord.gg/abc")]
    [InlineData("discord.gg/xivvirtue", "discord.gg/xivvirtue", "https://discord.gg/xivvirtue")]
    [InlineData("Live: www.twitch.tv/dj_x", "www.twitch.tv/dj_x", "https://www.twitch.tv/dj_x")]
    [InlineData("P52♥https:www.twitch.tv/dj_q", "https:www.twitch.tv/dj_q", "https://www.twitch.tv/dj_q")] // pf-0335
    [InlineData("http://old.example.org/a?b=1#c", "http://old.example.org/a?b=1#c", "http://old.example.org/a?b=1#c")]
    [InlineData("HTTPS://VENUE.CARRD.CO", "HTTPS://VENUE.CARRD.CO", "https://venue.carrd.co/")]
    [InlineData("site: www.example", "www.example", "https://www.example/")] // www. counts whatever the ending
    [InlineData("詳細→https://ffxiv.link/0145369", "https://ffxiv.link/0145369", "https://ffxiv.link/0145369")] // pf-0099
    [InlineData("httpbin.org", "httpbin.org", "https://httpbin.org/")] // starts like a scheme but isn't one
    public void ReadsTheLinkAndWhereItPoints(string ad, string text, string uri)
    {
        var link = Assert.Single(WebLinks.Find(ad));
        Assert.Equal(text, link.Text);
        Assert.Equal(uri, link.Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("Info: (mikoshisrest.com)", "mikoshisrest.com")] // pf-0244
    [InlineData("P42][https://darkaria.carrd.co]", "https://darkaria.carrd.co")] // pf-0202
    [InlineData("Plot 53. venue.carrd.co.", "venue.carrd.co")]
    [InlineData("see venue.com: open now", "venue.com")]
    [InlineData("is it venue.com?", "venue.com")]
    [InlineData("...venue.com", "venue.com")]
    [InlineData("https://cafecon.carrd.co/#", "https://cafecon.carrd.co/#")] // pf-0221: a trailing # and / stay
    [InlineData("discord.gg/a | prtk.gg/1", "discord.gg/a", "prtk.gg/1")] // pf-0016
    [InlineData("P58\rtwitch.tv/x", "twitch.tv/x")] // pf-0211
    [InlineData("discord.\uE035twitch.tv/x", "twitch.tv/x")] // ArrowDown is a line break (pf-0032)
    [InlineData("ｄｉｓｃｏｒｄ．ｇｇ／ａｂｃ", "discord.gg/abc")] // full-width letters
    public void LeavesOutTheTextAroundALink(string ad, params string[] expected) => Assert.Equal(expected, Texts(ad));

    [Theory]
    [InlineData("com")]
    [InlineData("net")]
    [InlineData("org")]
    [InlineData("co")]
    [InlineData("io")]
    [InlineData("me")]
    [InlineData("ee")]
    [InlineData("gg")]
    [InlineData("tv")]
    [InlineData("gd")]
    [InlineData("ly")]
    [InlineData("gl")]
    [InlineData("cc")]
    [InlineData("club")]
    [InlineData("site")]
    [InlineData("link")]
    [InlineData("xyz")]
    [InlineData("app")]
    [InlineData("dev")]
    [InlineData("page")]
    [InlineData("info")]
    [InlineData("bio")]
    [InlineData("live")]
    [InlineData("art")]
    [InlineData("us")]
    [InlineData("uk")]
    [InlineData("ca")]
    [InlineData("au")]
    [InlineData("nz")]
    [InlineData("eu")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("jp")]
    public void ALinkWithoutASchemeNeedsAKnownEnding(string ending)
    {
        Assert.Equal([$"venue.{ending}/a"], Texts($"see venue.{ending}/a"));
        Assert.Empty(WebLinks.Find($"see venue.{ending}x/a"));
    }

    [Theory]
    [InlineData("Lav.Beds W8 P58")]
    [InlineData("8.30 p.m. ST")]
    [InlineData("Emp.W15-P12")]
    [InlineData("e.g. dancers")]
    [InlineData("mail me at venue@mail.com")]
    [InlineData("javascript:alert(1).com")]
    [InlineData("ftp://files.example.com")]
    [InlineData("https://")]
    [InlineData("https://localhost/x")]
    [InlineData("https:/venue.com/a")] // one slash: the host is empty
    [InlineData("https://.com")]
    [InlineData("https://venue./a")]
    [InlineData("venue.")]
    [InlineData(".com")]
    [InlineData("Apply on discord.")]
    public void FindsNoLinkIn(string ad) => Assert.Empty(WebLinks.Find(ad));

    // Ads whose labelled URLs the parser doesn't give, each with the reason. Keep this list short.
    private static readonly Dictionary<string, string> KnownMisses = new()
    {
        ["pf-0388"] = "\"wahtaburger.my.canvas..site\": the double dot is not a valid host, so there is nowhere to open",
    };

    private static readonly Dictionary<string, (string Text, string[] Expected)> Ads = Load();

    private static Dictionary<string, (string, string[])> Load()
    {
        var ads = new Dictionary<string, (string, string[])>();
        foreach (var (id, root) in PfCorpus.Ads())
        {
            var expected = root.GetProperty("labels").GetProperty("urls").EnumerateArray().Select(u => u.GetString()!).ToArray();
            ads[id] = (root.GetProperty("text").GetString()!, expected);
        }

        return ads;
    }

    public static TheoryData<string> AdIds() => [.. Ads.Keys];

    /// <summary>Every corpus ad gives exactly its labelled URLs (Corpus/README.md, <c>urls</c>).</summary>
    [Theory]
    [MemberData(nameof(AdIds))]
    public void FindsTheLabelledUrls(string id)
    {
        var (text, expected) = Ads[id];
        var actual = Texts(text);

        if (KnownMisses.ContainsKey(id))
            Assert.False(expected.SequenceEqual(actual), $"{id} now parses correctly: remove it from KnownMisses");
        else
            Assert.Equal(expected, actual);
    }
}
