using Jaunt.Core.Browser;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class RecruitmentTests
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static bool OnlyRecruits(string text) =>
        Recruitment.OnlyRecruits(new ListingDetails([], []) { Recruits = Recruitment.Recruits(text) }, Schedules.ParseAd(text, Seen));

    [Theory]
    [InlineData("Mognet FC is recruiting! Casual, all welcome. Mist W12 P7")]
    [InlineData("Growing FC <CTHRS> looking for members! LB P50 W1")]
    [InlineData("Club Pulse is now hiring dancers, Goblet W3 P20")]
    [InlineData("Staff wanted: bartenders and security. Mist W5 P3")]
    [InlineData("Applications open for our tavern crew, Empyreum W2 P1")]
    [InlineData("Join Silly Gang FC on Phoenix at Lavender Beds, Plot 40, Ward 5!")]
    [InlineData("Come join our new Free Company, LB W3 P9")]
    [InlineData("FCメンバー募集中！エンピ10区アパ22号室")]
    [InlineData("スタッフ募集 ミスト 5区 3番地")]
    public void AnAdThatOnlyRecruitsIsDropped(string text) => Assert.True(OnlyRecruits(text));

    [Theory]
    [InlineData("Club Pulse tonight 9pm ST, now hiring dancers! Goblet W3 P20")] // a time
    [InlineData("Open now! Mist W5 P3, we're hiring")] // open now
    [InlineData("FC house open 24/7, new members welcome. Mist W5 P3")] // always open
    [InlineData("Fridays 9pm at the Lantern: staff and guests welcome, Mist W5 P3")] // a weekday and time
    public void ARecruitingAdThatStatesAnOpeningStays(string text) => Assert.False(OnlyRecruits(text));

    [Theory]
    [InlineData("Cozy cafe, come hang out! Mist W5 P3")]
    [InlineData("Our FC house is a venue for everyone, Mist W5 P3")] // an FC, nobody asked to join
    [InlineData("Join us at the Lantern, Mist W5 P3")] // join, no group
    [InlineData("Lsd Cafe, Mist W5 P3")] // "ls" only as a whole word
    [InlineData("在庫は募集ロール参照 ラベンダーベッド27-51")] // the party's own slots
    [InlineData("")]
    public void AnAdThatDoesntRecruitStays(string text) => Assert.False(Recruitment.Recruits(text));

    [Theory]
    [InlineData("We are hiring!")]
    [InlineData("Members only lounge")] // "members" without the s
    [InlineData("RECRUITING")] // any case
    [InlineData("Applications: discord.gg/x")]
    [InlineData("Join our CWLS")]
    [InlineData("Linkshell: join via tell")] // join anywhere in the text
    public void RecruitmentWordsAreRead(string text) => Assert.True(Recruitment.Recruits(text));

    [Fact]
    public void AListingThatIsntAnAdNeverRecruits() =>
        Assert.False(Recruitment.OnlyRecruits(new ListingDetails([], []), []));

    private static readonly Dictionary<string, (string Text, DateTimeOffset Seen, bool Recruiting)> AddressedAds = ReadAddressedAds();

    // Ads without a housing address aren't listed anyway, so only those with one are checked
    private static Dictionary<string, (string, DateTimeOffset, bool)> ReadAddressedAds()
    {
        var ads = new Dictionary<string, (string, DateTimeOffset, bool)>();
        foreach (var (id, root) in PfCorpus.Ads())
        {
            var labels = root.GetProperty("labels");
            if (labels.GetProperty("addresses").GetArrayLength() == 0)
                continue;
            ads[id] = (root.GetProperty("text").GetString()!,
                DateTimeOffset.Parse(root.GetProperty("firstSeen").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                labels.GetProperty("kind").GetString() == "recruiting");
        }

        return ads;
    }

    public static TheoryData<string> AdsWithAddresses() => [.. AddressedAds.Keys];

    [Theory]
    [MemberData(nameof(AdsWithAddresses))]
    public void CorpusAdsAreDroppedExactlyWhenLabelledRecruiting(string id)
    {
        var (text, seen, recruiting) = AddressedAds[id];
        var details = new ListingDetails([], []) { Recruits = Recruitment.Recruits(text) };
        Assert.Equal(recruiting, Recruitment.OnlyRecruits(details, Schedules.ParseAd(text, seen)));
    }
}
