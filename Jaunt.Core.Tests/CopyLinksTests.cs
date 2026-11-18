using Jaunt.Core.Browser;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class CopyLinksTests
{
    private static WebLink[] Links(params string[] uris) => [.. uris.Select(u => new WebLink(u, new Uri(u)))];

    private static (ListingSource, string)[] Members(params (ListingSource, string)[] members) => members;

    [Fact]
    public void TheOwnWebsiteComesFirst()
    {
        var copy = CopyLinks.Choose(
            Links("https://discord.gg/abc", "https://twitch.tv/dj", "https://partake.gg/events/9", "https://venue.carrd.co/"),
            Members((ListingSource.Partake, "9"), (ListingSource.FfxivVenues, "v1"), (ListingSource.InGamePartyFinder, "123")));
        Assert.Equal(new CopyLink(CopyLinkKind.Website, new Uri("https://venue.carrd.co/")), copy);
    }

    [Theory]
    [InlineData("https://discord.gg/abc")]
    [InlineData("https://discord.com/invite/abc")]
    [InlineData("https://www.twitch.tv/dj")]
    [InlineData("https://x.com/venue")]
    [InlineData("https://ffxivvenues.com/venue/abc")]
    [InlineData("https://prtk.gg/1")]
    public void StreamsProfilesInvitesAndListingSitesArentTheOwnWebsite(string uri) =>
        Assert.NotEqual(CopyLinkKind.Website, CopyLinks.Choose(Links(uri), []).Kind);

    [Fact]
    public void TheCalendarsForwarderCountsAsTheOrganisersPage() =>
        Assert.Equal(CopyLinkKind.Website, CopyLinks.Choose(Links("https://xiv.fyi/gbL8"), []).Kind);

    [Fact]
    public void ThePartakeEventPageComesBeforeTheVenuePage()
    {
        var copy = CopyLinks.Choose(
            Links("https://discord.gg/abc"), Members((ListingSource.FfxivVenues, "v1"), (ListingSource.Partake, "1234")));
        Assert.Equal(new CopyLink(CopyLinkKind.EventPage, new Uri("https://partake.gg/events/1234")), copy);
    }

    [Theory]
    [InlineData("https://prtk.gg/77")]
    [InlineData("https://partake.gg/events/77")]
    [InlineData("https://www.partake.gg/events/77")]
    public void AnEventPageAnAdLinksCounts(string uri) =>
        Assert.Equal(
            new CopyLink(CopyLinkKind.EventPage, new Uri(uri)),
            CopyLinks.Choose(Links(uri), Members((ListingSource.FfxivVenues, "v1"))));

    [Fact]
    public void AHostEndingInAListingSitesNameIsntTheSite() =>
        Assert.Equal(
            new CopyLink(CopyLinkKind.Website, new Uri("https://notffxivvenues.com/")),
            CopyLinks.Choose(Links("https://notffxivvenues.com/"), []));

    [Fact]
    public void OnlyWebLinksAreCopied() =>
        Assert.Equal(new CopyLink(CopyLinkKind.None), CopyLinks.Choose(Links("ftp://ffxivvenues.com/venue/x", "ftp://partake.gg/e"), []));

    [Fact]
    public void AnFfxivVenuesMembersPageIsTheVenuePage()
    {
        var copy = CopyLinks.Choose([], Members((ListingSource.Xivpf, "42"), (ListingSource.FfxivVenues, "a b")));
        Assert.Equal(new CopyLink(CopyLinkKind.VenuePage, new Uri("https://ffxivvenues.com/venue/a%20b")), copy);
    }

    [Fact]
    public void AVenuePageAnAdLinksCounts() =>
        Assert.Equal(
            new CopyLink(CopyLinkKind.VenuePage, new Uri("https://www.ffxivvenues.com/venue/x")),
            CopyLinks.Choose(Links("https://www.ffxivvenues.com/venue/x"), Members((ListingSource.InGamePartyFinder, "1"))));

    [Fact]
    public void NothingWithoutAPage() =>
        Assert.Equal(
            new CopyLink(CopyLinkKind.None),
            CopyLinks.Choose(
                Links("https://discord.gg/abc", "https://twitch.tv/dj"),
                Members((ListingSource.RpEventCalendar, "e"), (ListingSource.Shout, "s"), (ListingSource.InGamePartyFinder, "123"), (ListingSource.Xivpf, "456"))));

    [Fact]
    public void AnEmptyIdHasNoPage() =>
        Assert.Equal(new CopyLink(CopyLinkKind.None), CopyLinks.Choose([], Members((ListingSource.FfxivVenues, ""), (ListingSource.Partake, ""))));
}
