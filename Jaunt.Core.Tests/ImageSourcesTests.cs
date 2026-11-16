using Jaunt.Core.Images;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ImageSourcesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);
    private const string Banner = "https://images.ffxivvenues.com/abc/KqB9";

    private static CachedListing Venue(string? banner = Banner, string? website = null)
    {
        var listing = new ListingCache().Seen(ListingSource.FfxivVenues, "abc", "Lounge", Now);
        listing.Venue = new FfxivVenues.Fields(null, website, null, null, BannerUri: banner);
        return listing;
    }

    private static CachedListing Event(string[]? attachments = null, string[]? images = null, string[]? links = null)
    {
        var listing = new ListingCache().Seen(ListingSource.Partake, "1", "Gala", Now, oneOff: true);
        listing.Attachments = attachments ?? [];
        listing.DescriptionImages = images ?? [];
        listing.LinkTexts = links ?? [];
        return listing;
    }

    private static CachedListing Ad(ListingSource source = ListingSource.InGamePartyFinder) =>
        new ListingCache().SeenAd(source, "ad", "Lounge Mist W1 P1 lounge.example discord.gg/ad", "Siren", Now);

    private static Uri[] Links(params string[] links) => [.. links.Select(l => new Uri(l))];

    private static string[] Keys(IReadOnlyList<ImageSource> sources) => [.. sources.Select(s => s.Key)];

    [Fact]
    public void AnIconIsTheDiscordServersThenTheVenuesWebsitesThenTheBanner()
    {
        var sources = ImageSources.For(
            ImageKind.Icon, [Venue(website: "lounge.carrd.co/menu")], Links("https://lounge.carrd.co/menu", "https://discord.gg/lounge"));

        Assert.Equal(["discord:lounge", "favicon:https://lounge.carrd.co/", "url:" + Banner], Keys(sources));
    }

    [Fact]
    public void ASplashIsTheBannerThenTheDiscordServersThenThePartakeImages()
    {
        var sources = ImageSources.For(
            ImageKind.Splash, [Venue(), Event(["a.webp"], ["https://cdn.partake.gg/assets/b.webp"])],
            Links("https://lounge.carrd.co/", "https://discord.com/invite/Lounge-2"));

        Assert.Equal(
            ["url:" + Banner, "discord:Lounge-2", "url:https://cdn.partake.gg/assets/a.webp", "url:https://cdn.partake.gg/assets/b.webp"],
            Keys(sources));
    }

    [Theory]
    [InlineData(ListingSource.InGamePartyFinder)]
    [InlineData(ListingSource.Xivpf)]
    [InlineData(ListingSource.RpEventCalendar)]
    public void WebsitesFromOtherSourcesGiveNoIconButTheirInvitesDo(ListingSource source)
    {
        // Any player writes an ad's links, so fetching its website would tell them who scrolled past
        var listing = source == ListingSource.RpEventCalendar ? new ListingCache().Seen(source, "e", "Gala", Now) : Ad(source);
        listing.LinkTexts = ["https://lounge.example/"];

        var sources = ImageSources.For(ImageKind.Icon, [listing], Links("https://lounge.example/", "https://discord.gg/ad"));

        Assert.Equal(["discord:ad"], Keys(sources));
    }

    [Fact]
    public void APartakeHostsWebsiteGivesAnIconUnlessItIsAPersonsOrAnEventsPage()
    {
        var sources = ImageSources.For(
            ImageKind.Icon, [Event(links: ["https://twitch.tv/dj", "https://partake.gg/teams/1", "discord.gg/host", "http://www.lounge.example:8080/a?b"])], []);

        Assert.Equal(["favicon:http://www.lounge.example:8080/"], Keys(sources));
    }

    [Fact]
    public void AtMostTwoOfEachKindAreTriedEachOnce()
    {
        var sources = ImageSources.For(
            ImageKind.Icon,
            [
                Venue(website: "https://one.example/"), Venue("https://images.ffxivvenues.com/b", "https://one.example/x"),
                Venue("https://images.ffxivvenues.com/c", "https://two.example/"), Venue(website: "https://three.example/"),
            ],
            Links("https://discord.gg/a", "https://discord.gg/a", "https://discord.gg/b", "https://discord.gg/c"));

        Assert.Equal(
            ["discord:a", "discord:b", "favicon:https://one.example/", "favicon:https://two.example/", "url:" + Banner, "url:https://images.ffxivvenues.com/b"],
            Keys(sources));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a link")]
    [InlineData("ftp://images.example/a.png")]
    [InlineData("file:///C:/secret.png")]
    public void ABannerThatIsntAWebAddressIsSkipped(string? banner)
    {
        Assert.Empty(ImageSources.For(ImageKind.Splash, [Venue(banner)], []));
    }

    [Fact]
    public void PartakeImagesThatArentWebAddressesOrPlainNamesAreSkipped()
    {
        var sources = ImageSources.For(ImageKind.Splash, [Event(["../x.png", "ok"], ["javascript:alert(1)", "relative.png"])], []);

        Assert.Equal(["url:https://cdn.partake.gg/assets/ok.webp"], Keys(sources));
    }

    [Fact]
    public void AnInviteCodeThatIsntSafeInAPathIsSkipped()
    {
        Assert.Empty(ImageSources.For(ImageKind.Icon, [], Links("https://discord.gg/a%2F..%2Fusers", "https://discord.gg/" + new string('a', 33))));
        Assert.Single(ImageSources.For(ImageKind.Icon, [], Links("https://discord.gg/" + new string('a', 32))));
    }

    [Fact]
    public void AnEntryWithNothingToShowHasNoSources()
    {
        Assert.Empty(ImageSources.For(ImageKind.Icon, [Event()], []));
        Assert.Empty(ImageSources.For(ImageKind.Splash, [Event(links: ["https://lounge.example/"])], Links("https://twitch.tv/dj")));
    }
}
