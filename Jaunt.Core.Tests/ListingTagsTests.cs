using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ListingTagsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static Interests InterestsOf(string text) => ListingTags.Read(text).Interests;

    private static CachedListing Ad(string text) => new() { Source = ListingSource.Xivpf, Id = "1", Description = text };

    private static CachedListing Venue(bool? sfw, params string?[] tags) => new()
    {
        Source = ListingSource.FfxivVenues,
        Id = "v",
        Title = "The Test Lounge",
        Venue = new FfxivVenues.Fields(null, null, null, null, sfw, tags),
    };

    private static CachedListing PartakeEvent(string title, string? rating, params string[] tags) => new()
    {
        Source = ListingSource.Partake,
        Id = "p",
        Title = title,
        Rating = rating,
        SourceTags = tags,
    };

    private static CachedListing CalendarEvent(string name, string? rating) => new()
    {
        Source = ListingSource.RpEventCalendar,
        Id = "c",
        Title = name,
        Rating = rating,
    };

    [Theory]
    [InlineData("DJ Damsca tonight", Interests.Clubs)]
    [InlineData("Live: twitch.tv/somedj", Interests.Clubs)]
    [InlineData("Dark alt lounge, cocktails", Interests.Bars)]
    [InlineData("Maid cafe with fresh coffee", Interests.Cafes)]
    [InlineData("Open Stage all night", Interests.LiveShows)]
    [InlineData("Giggle Fits Comedy open Tues at 8pm EST", Interests.LiveShows)]
    [InlineData("Looking for more rotating comedians!", Interests.LiveShows)]
    [InlineData("Stand-up night", Interests.LiveShows)]
    [InlineData("Standup and improv", Interests.LiveShows)]
    [InlineData("☆The Nightmare Before Starlight☆ theater♢music", Interests.LiveShows)]
    [InlineData("Community theatre tonight", Interests.LiveShows)]
    [InlineData("Gamba, DRT and a raffle", Interests.Gambling)]
    [InlineData("Bingo and trivia, prizes to win", Interests.Games)]
    [InlineData("50/50 at midnight", Interests.Gambling)]
    [InlineData("Heavy RP, walk-ins welcome", Interests.Roleplay)]
    [InlineData("Indoor bathhouse with hot springs", Interests.Spa)]
    [InlineData("Art gallery opening", Interests.Art)]
    [InlineData("Free gpose sessions", Interests.Photography)]
    [InlineData("Tarot readings by the fire", Interests.Tarot)]
    [InlineData("Open 24/7, AFK friendly", Interests.Chill)]
    [InlineData("喫茶 ひだまり", Interests.Cafes)]
    [InlineData("雑談しましょう", Interests.Chill)]
    [InlineData("喫茶 珈琲", Interests.Cafes)] // two Japanese keywords with one interest
    [InlineData("Truth or dare tonight", Interests.Games)]
    [InlineData("Fae-inspired Bar, Fight Ring!", Interests.Bars | Interests.Games)]
    [InlineData("Bounties, Brawls and a Wrestling Pit", Interests.Games)]
    [InlineData("Through Darkness or Light, we fight together", Interests.None)] // "fight" alone isn't a fight night
    [InlineData("LavB W25/P6/Bar", Interests.Bars)] // "/" between a digit and a letter splits words
    public void KeywordsTagTheirInterest(string text, Interests expected) => Assert.Equal(expected, InterestsOf(text));

    [Fact]
    public void AnAdUsuallyHasSeveralInterests() =>
        Assert.Equal(Interests.Clubs | Interests.Bars | Interests.Gambling | Interests.Tarot, InterestsOf("Bar/DJS/Tarot/Oracle readings, gamba"));

    [Theory]
    [InlineData("Party starts at 9")] // "art" inside "starts" and "party"
    [InlineData("Our team is open")] // "tea" inside "team"
    [InlineData("Barrel of laughs")] // "bar" inside "barrel"
    [InlineData("Ward 24 plot 7")] // 24 and 7 apart aren't 24/7
    [InlineData("Open the stage door")] // a phrase's words must be consecutive
    [InlineData("Open stagehands wanted")] // a phrase's words are whole words too
    [InlineData("Come play whose line, no drama")] // play, drama and line aren't keywords
    [InlineData("")]
    public void KeywordsMatchWholeWordsOnly(string text) => Assert.Equal(Interests.None, InterestsOf(text) & ~Interests.Clubs);

    [Fact]
    public void PluralsMatchTheirKeyword() => Assert.Equal(Interests.Bars | Interests.Spa, InterestsOf("Two bars and hot springs"));

    [Fact]
    public void BoxedLettersAreReadAsLetters()
    {
        // B A R in the game's boxed letters (U+E071 is A)
        var boxed = new string([(char)(0xE071 + 1), (char)0xE071, (char)(0xE071 + 17)]);

        Assert.Equal(Interests.Bars, InterestsOf($"{boxed} open now"));
    }

    [Theory]
    [InlineData("Club Noir open tonight", Interests.Clubs)]
    [InlineData("The Bubblegum Club", Interests.Clubs)]
    [InlineData("Two clubs, one night", Interests.Clubs)]
    [InlineData("18+ The Wildwood Club now open! | Fae bathhouse and social club for all.", Interests.Spa)] // the bare "club" is the social club
    [InlineData("Immersive RP, Bar + Host Club Open NOW!", Interests.Roleplay | Interests.Bars)]
    [InlineData("Eden Hall Hostess Club", Interests.None)]
    [InlineData("A gentleman's club", Interests.None)]
    [InlineData("Gentlemens Club", Interests.None)]
    [InlineData("Book clubs on Sunday", Interests.None)]
    [InlineData("Fight club tonight", Interests.Games)]
    [InlineData("Social club with a DJ", Interests.Clubs)] // other club words still count
    public void ClubMeansANightclubUnlessTheTextSaysAnotherKind(string text, Interests expected) => Assert.Equal(expected, InterestsOf(text));

    [Theory]
    [InlineData("Come party with us", true)]
    [InlineData("Come with us", false)]
    public void PartyCountsAsAClubNight(string text, bool club) => Assert.Equal(club, InterestsOf(text).HasFlag(Interests.Clubs));

    [Theory]
    [InlineData("[NSFW] lounge", true)]
    [InlineData("SFW lounge", false)]
    [InlineData("SFW bar with an NSFW back room", true)]
    [InlineData("A lounge", null)]
    [InlineData("nsfwish lounge", null)]
    public void ExplicitWordsSaySfwOrNsfw(string text, bool? nsfw) => Assert.Equal(nsfw, ListingTags.Read(text).Nsfw);

    [Theory]
    [InlineData("18+ lounge", true)]
    [InlineData("(21+) lounge", true)]
    [InlineData("+18 only", true)]
    [InlineData("+21 only", true)]
    [InlineData("R18+ event", true)]
    [InlineData("Adult only tonight", true)]
    [InlineData("18+ONLY tonight", true)]
    [InlineData("R18 event", true)]
    [InlineData("R-18 event", true)]
    [InlineData("Adults only tonight", true)]
    [InlineData("18禁イベント", true)]
    [InlineData("R-18, adults only", true)] // two age phrases
    [InlineData("18禁 成人向け", true)] // two Japanese age words
    [InlineData("Ward 18 plot 21", false)]
    [InlineData("W18 P21", false)]
    [InlineData("Open 18:00", false)]
    public void AgeWordsMarkAnAgeRestriction(string text, bool restricted) => Assert.Equal(restricted, ListingTags.Read(text).AgeRestricted);

    [Theory]
    [InlineData("18+ lounge", 18)]
    [InlineData("21+ lounge", 21)]
    [InlineData("+21 only", 21)]
    [InlineData("R18 event", 18)]
    [InlineData("Adults only tonight", 18)]
    [InlineData("18禁イベント", 18)]
    [InlineData("21+ bar, 18+ dance floor", 21)] // the highest wins, whichever comes first
    [InlineData("18+ dance floor, 21+ bar", 21)]
    [InlineData("+21 bar, 18+ dance floor", 21)]
    [InlineData("21+ bar, adults only", 21)]
    [InlineData("21+ bar, 18禁", 21)]
    [InlineData("Ward 18 plot 21", 0)]
    public void AgeWordsGiveTheAge(string text, int age) => Assert.Equal(age, ListingTags.Read(text).MinAge);

    [Theory]
    [InlineData("SFW 18+ lounge", "18+")]
    [InlineData("(21+) lounge", "21+")]
    [InlineData("Chill lounge", null)]
    public void AnAdsAgeIsTheRestrictionItGives(string text, string? age) => Assert.Equal(age, ListingTags.AgeOf(Ad(text)));

    [Theory]
    [InlineData("Club night", "MATURE", "Mature")]
    [InlineData("Club night", " ADULT ", "Adult")]
    [InlineData("Club night", "EVERYONE", "Everyone")]
    [InlineData("Club night 18+", null, "18+")] // no rating: the title's age
    [InlineData("Club night 18+", "", "18+")]
    [InlineData("Club night", null, null)]
    public void APartakeEventsAgeIsItsRating(string title, string? rating, string? age) => Assert.Equal(age, ListingTags.AgeOf(PartakeEvent(title, rating)));

    [Theory]
    [InlineData("Tavern night", "Mature", "Mature")]
    [InlineData("[AO] Tavern night", null, "Adults Only")] // the name's tag when no rating is sent
    [InlineData("[AO] Tavern night", "Teen", "Teen")] // the rating sent wins over the tag
    [InlineData("Tavern night 18+", "Mature", "Mature")] // the rating wins over the name's age
    [InlineData("Tavern night 18+", " ", "18+")]
    [InlineData("Tavern night", null, null)]
    public void ACalendarEventsAgeIsItsRating(string name, string? rating, string? age) => Assert.Equal(age, ListingTags.AgeOf(CalendarEvent(name, rating)));

    [Fact]
    public void AVenueGivesNoAge()
    {
        var venue = Venue(false);
        venue.Title = "The 18+ Lounge";
        Assert.Null(ListingTags.AgeOf(venue));
    }

    [Theory]
    [InlineData("18+ lounge", true)]
    [InlineData("18+ SFW lounge", false)] // an explicit word wins
    [InlineData("NSFW lounge", true)]
    [InlineData("Lounge", false)]
    public void AnAdIsNsfwWhenItSaysSoOrGivesAnAgeWithoutSayingSfw(string text, bool nsfw) => Assert.Equal(nsfw, ListingTags.Of(Ad(text)).Nsfw);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)] // cached before sfw was kept
    public void AVenuesSfwFieldDecides(bool? sfw, bool nsfw) => Assert.Equal(nsfw, ListingTags.Of(Venue(sfw)).Nsfw);

    [Fact]
    public void AVenueWithoutTagsTakesItsNamesKeywords() =>
        Assert.Equal(Interests.Bars, ListingTags.Of(new CachedListing
        {
            Source = ListingSource.FfxivVenues,
            Id = "v",
            Title = "The Test Lounge",
            Venue = new FfxivVenues.Fields(null, null, null, null),
        }).Interests);

    [Fact]
    public void TwoTagsWithOneInterestKeepIt() =>
        Assert.Equal(Interests.Bars, ListingTags.Of(new CachedListing
        {
            Source = ListingSource.FfxivVenues,
            Id = "v",
            Title = "Nowhere",
            Venue = new FfxivVenues.Fields(null, null, null, null, true, ["Bar", "Drink"]),
        }).Interests);

    [Fact]
    public void EveryKeywordTagsItsInterest()
    {
        var missed = ListingTags.Keywords.Where(k => !ListingTags.Read(k.Keyword).Interests.HasFlag(k.Interests)).Select(k => k.Keyword).ToList();

        Assert.True(missed.Count == 0, $"never match: {string.Join(", ", missed)}");
    }

    [Fact]
    public void EverySourceTagIsOneTheSourcesSend()
    {
        var unknown = ListingTags.SourceTagInterests.Keys.Where(t => !SentTags.Contains(t)).ToList();

        Assert.True(unknown.Count == 0, $"not sent by FFXIV Venues or Partake: {string.Join(", ", unknown)}");
    }

    // The tags FFXIV Venues (all venues) and Partake (120 events) sent on 2026-11-15
    private static readonly HashSet<string> SentTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Drink", "Bar", "Food", "RP Heavy", "Nightclub", "Photography", "Twitch DJ", "Lounge", "Dancers", "Gambling",
        "Courtesans", "Syncshell available", "Bards", "VIP", "Open stage", "LGBTQIA+", "Tavern", "Cafe", "IC RP Only",
        "Performances", "Pillow", "Bath house", "Other", "Artists", "Tarot", "Deathroll", "Blackjack", "24/7 open house",
        "Triple triad", "Giveaways", "Restaurant", "Casino", "Sync DJ", "Void", "Den", "Other Games", "Truth or dare",
        "Bingo", "Maid cafe", "Inn", "Shop", "Fightclub", "Roulette", "Stylists", "Novel services", "Texas holdem",
        "Shows and Performances", "Library", "None", "Soft-counselling", "Auction house",
        "Nightlife & Entertainment", "Character Sync", "Live DJs", "Photography Services", "LGBTQ+ Friendly",
        "Mixed IC/OOC", "Gambling Available", "Includes giveaways", "Courtesan Services", "Bars & Lounges",
        "Casino & Gambling", "Casual RP", "VIP Services", "Mature Nightlife", "Community & Hangouts", "Live Performances",
        "Lightless Sync", "Cafes & Dining", "Roleplay & Story", "No RP (OOC)", "Tournaments & Competitions",
        "Performance & Shows", "Heavy RP", "Creative & Showcase", "Spa & Relaxation", "Fully IC", "Cafes, Taverns & Inns",
    };

    [Fact]
    public void AVenueTakesItsOwnTagsIgnoringCaseAndUnknownOnes() =>
        Assert.Equal(
            Interests.Spa | Interests.Tarot | Interests.Chill | Interests.Bars,
            ListingTags.Of(Venue(true, "bath house", " Tarot ", "24/7 open house", "Courtesans", null)).Interests);

    [Theory]
    [InlineData("Bar Night", "ADULT", true)]
    [InlineData("Bar Night", "adult ", true)]
    [InlineData("Bar Night", "MATURE", false)]
    [InlineData("Bar Night", "EVERYONE", false)]
    [InlineData("Bar Night", null, false)]
    [InlineData("NSFW Bar Night", "MATURE", true)]
    [InlineData("SFW Bar Night", "ADULT", false)] // an explicit word wins over the rating
    public void APartakeEventIsNsfwWhenAdultOrItsTitleSaysSo(string title, string? rating, bool nsfw) =>
        Assert.Equal(nsfw, ListingTags.Of(PartakeEvent(title, rating)).Nsfw);

    [Fact]
    public void APartakeEventTakesItsTagsAndTitle() =>
        Assert.Equal(
            Interests.Clubs | Interests.Gambling | Interests.Cafes | Interests.Bars | Interests.Tarot,
            ListingTags.Of(PartakeEvent("Tarot Tuesday", null, "Live DJs", "casino & gambling", "Cafes, Taverns & Inns", "LGBTQ+ Friendly")).Interests);

    [Theory]
    [InlineData("Moonlit Masquerade", "Adults Only", true)]
    [InlineData("[AO] Moonlit Masquerade", null, true)]
    [InlineData("Moonlit Masquerade", "Mature", false)]
    [InlineData("[M] Moonlit Masquerade", null, false)]
    [InlineData("Moonlit Masquerade (NSFW)", "Mature", true)]
    [InlineData("Moonlit Masquerade", "Teen", false)]
    public void AnRpCalendarEventIsNsfwWhenAdultsOnlyOrItsNameSaysSo(string name, string? rating, bool nsfw) =>
        Assert.Equal(nsfw, ListingTags.Of(CalendarEvent(name, rating)).Nsfw);

    [Fact]
    public void AnRpCalendarEventIsAlwaysRoleplay() =>
        Assert.Equal(Interests.Roleplay | Interests.Bars, ListingTags.Of(CalendarEvent("[T] Tavern Night", "Teen")).Interests);

    [Fact]
    public void ListingsShownAsOneTakeEveryMembersInterests() =>
        Assert.Equal(
            Interests.Bars | Interests.Games | Interests.Tarot,
            ListingTags.Combine([
                new Tagging(Interests.Bars, false),
                new Tagging(Interests.Games | Interests.Bars, false),
                new Tagging(Interests.Tarot, false),
            ]).Interests);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)] // NSFW wins, even over an FFXIV Venues venue marked SFW
    [InlineData(true, true, true)]
    public void AnyNsfwMemberMakesListingsShownAsOneNsfw(bool firstNsfw, bool secondNsfw, bool nsfw) =>
        Assert.Equal(nsfw, ListingTags.Combine([new Tagging(Interests.None, firstNsfw), new Tagging(Interests.Roleplay, secondNsfw)]).Nsfw);

    [Fact]
    public void ACachedAdIsTaggedFromItsText()
    {
        var cache = new ListingCache();
        var ad = cache.SeenAd(ListingSource.InGamePartyFinder, "1", "18+ Bathhouse open now", null, Now);

        Assert.Equal(new Tagging(Interests.Spa, Nsfw: true), ListingTags.Of(ad));
    }
}
