using System.Text.Json;
using Jaunt.Core.Listings;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class ListingCacheTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SeeingAListingAgainUpdatesItInsteadOfAddingOne()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "Old title", Now.AddDays(-2));

        var listing = cache.Seen(ListingSource.Xivpf, "1", "New title", Now);

        Assert.Equal(1, cache.Count);
        Assert.Equal("New title", listing.Title);
        Assert.Equal(Now, listing.LastSeen);
    }

    [Fact]
    public void AnAdIsTitledFromItsTextAndKeepsItsTextAndWorld()
    {
        const string ad = "Moonlit Lounge  Mist W14 P33\nmoonlit.carrd.co";
        var cache = new ListingCache();

        var listing = cache.SeenAd(ListingSource.InGamePartyFinder, "7", ad, "Cuchulainn", Now);

        Assert.Same(listing, cache.Find(ListingSource.InGamePartyFinder, "7"));
        Assert.Equal(ListingDetails.Title(ad), listing.Title);
        Assert.Equal(ad, listing.Description);
        Assert.Equal("Cuchulainn", listing.ListedOn);
        Assert.Equal(Now, listing.LastSeen);
        Assert.False(listing.OneOff);
    }

    [Fact]
    public void SeeingAnAdAgainReplacesItsTextAndWorldAndKeepsMarks()
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.InGamePartyFinder, "7", "Old text", "Cuchulainn", Now.AddHours(-1)).Favourite = true;

        var listing = cache.SeenAd(ListingSource.InGamePartyFinder, "7", "New text", null, Now);

        Assert.Equal(1, cache.Count);
        Assert.Equal("New text", listing.Title);
        Assert.Equal("New text", listing.Description);
        Assert.Null(listing.ListedOn);
        Assert.True(listing.Favourite);
    }

    [Fact]
    public void TheSameIdFromAnotherSourceIsAnotherListing()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", Now);
        cache.Seen(ListingSource.InGamePartyFinder, "1", "B", Now);

        Assert.Equal(2, cache.Count);
        Assert.Equal("B", cache.Find(ListingSource.InGamePartyFinder, "1")!.Title);
    }

    [Fact]
    public void AnOlderSightingDoesNotMoveLastSeenBack()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", Now);

        var listing = cache.Seen(ListingSource.Xivpf, "1", "A", Now.AddHours(-1));

        Assert.Equal(Now, listing.LastSeen);
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void ClearRemovesOnlyUnmarkedListings(bool favourite, bool visited, bool hidden, bool removed)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Xivpf, "1", "A", Now);
        listing.Favourite = favourite;
        listing.Visited = visited;
        listing.Hidden = hidden;

        var count = cache.ClearUnmarked();

        Assert.Equal(removed ? 1 : 0, count);
        Assert.Equal(removed, cache.Find(ListingSource.Xivpf, "1") is null);
    }

    [Fact]
    public void ClearSaysHowManyItRemoved()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", Now);
        cache.Seen(ListingSource.Xivpf, "2", "B", Now);
        cache.Seen(ListingSource.Xivpf, "3", "C", Now).Hidden = true;

        Assert.Equal(2, cache.ClearUnmarked());
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    // One calendar month: 14 Oct 20:00 expires at 14 Nov 20:00
    [InlineData("2026-10-14T20:00:00Z", true)]
    [InlineData("2026-10-14T20:00:01Z", false)]
    [InlineData("2026-09-01T00:00:00Z", true)]
    [InlineData("2026-11-13T00:00:00Z", false)]
    public void UnmarkedListingsExpireAMonthAfterTheyWereLastSeen(string lastSeen, bool expired)
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "A", DateTimeOffset.Parse(lastSeen, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expired ? 1 : 0, cache.RemoveExpired(Now));
        Assert.Equal(expired ? 0 : 1, cache.Count);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MarkedListingsNeverExpire(bool favourite, bool visited, bool hidden)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Xivpf, "1", "A", Now.AddYears(-1));
        listing.Favourite = favourite;
        listing.Visited = visited;
        listing.Hidden = hidden;

        Assert.Equal(0, cache.RemoveExpired(Now));
        Assert.Equal(1, cache.Count);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MarkedOneOffsExpireAMonthAfterTheyWereLastSeen(bool favourite, bool visited, bool hidden)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddMonths(-1), oneOff: true);
        listing.Favourite = favourite;
        listing.Visited = visited;
        listing.Hidden = hidden;

        Assert.Equal(1, cache.RemoveExpired(Now));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void AMarkedOneOffSeenWithinTheMonthIsKept()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddMonths(-1).AddSeconds(1), oneOff: true).Favourite = true;

        Assert.Equal(0, cache.RemoveExpired(Now));
    }

    [Fact]
    public void ClearKeepsMarkedOneOffs()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now, oneOff: true).Visited = true;

        Assert.Equal(0, cache.ClearUnmarked());
    }

    [Fact]
    public void TheLatestSightingSaysWhetherItIsAOneOff()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddDays(-1), oneOff: true);

        Assert.False(cache.Seen(ListingSource.Partake, "1", "Gala", Now).OneOff);
    }

    [Fact]
    public void HiddenListingsAreListedByTitle()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Xivpf, "1", "zebra lounge", Now).Hidden = true;
        cache.Seen(ListingSource.Xivpf, "2", "Not hidden", Now);
        cache.Seen(ListingSource.FfxivVenues, "3", "Amber Room\nBar & Grill", Now).Hidden = true;
        cache.Seen(ListingSource.RpEventCalendar, "4", "[M] Midnight Market", Now).Hidden = true;

        var hidden = cache.HiddenListings();

        // An RP calendar event sorts by its title as shown, without the rating tag
        Assert.Equal(["Amber Room", "Midnight Market", "zebra lounge"], hidden.Select(l => l.ShownTitle));
    }

    [Fact]
    public void AnAdIsTitledFromItsLatestText()
    {
        var cache = new ListingCache();
        var listing = cache.SeenAd(ListingSource.Xivpf, "1", "Moonlit Lounge", null, Now);
        Assert.Equal("Moonlit Lounge", listing.Title);

        cache.SeenAd(ListingSource.Xivpf, "1", "Sunlit Lounge\nMist W1 P2", null, Now);

        Assert.Equal("Sunlit Lounge", listing.Title);
    }

    [Fact]
    public void SavedCacheLoadsBackWithItsMarks()
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.Partake, "42", "ラベンダー Café", Now, oneOff: true);
        listing.StartsAt = Now.AddHours(1);
        listing.EndsAt = Now.AddHours(4);
        listing.ListedOn = "Lich";
        listing.DataCentre = "Light";
        listing.Location = "LB W19 P58";
        listing.LinkTexts = ["discord.gg/gala"];
        listing.Favourite = true;
        listing.Visited = true;
        cache.Seen(ListingSource.Xivpf, "7", "Hidden one", Now.AddDays(-3)).Hidden = true;

        var loaded = SavedFiles.RoundTrip(cache);

        Assert.Equal(2, loaded.Count);
        var back = loaded.Find(ListingSource.Partake, "42")!;
        Assert.Equal("ラベンダー Café", back.Title);
        Assert.Equal(Now, back.LastSeen);
        Assert.True(back.OneOff);
        Assert.Equal(Now.AddHours(1), back.StartsAt);
        Assert.Equal(Now.AddHours(4), back.EndsAt);
        Assert.Equal("Lich", back.ListedOn);
        Assert.Equal("Light", back.DataCentre);
        Assert.Equal("LB W19 P58", back.Location);
        Assert.Equal(["discord.gg/gala"], back.LinkTexts);
        Assert.True(back.Favourite);
        Assert.True(back.Visited);
        Assert.False(back.Hidden);
        // An ad without text keeps its title in the marks file only
        var ad = loaded.Find(ListingSource.Xivpf, "7")!;
        Assert.Equal(("Hidden one", false, true, Now.AddDays(-3)), (ad.Title, ad.OneOff, ad.Hidden, ad.LastSeen));
        Assert.Null(ad.StartsAt);
    }

    [Fact]
    public void EachPartIsSavedInItsOwnFileWithAVersion()
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.InGamePartyFinder, "1", "Ad", null, Now).Favourite = true;
        cache.Seen(ListingSource.FfxivVenues, "2", "Venue", Now);
        cache.Fetched(ListingSource.FfxivVenues, Now, "Aether");

        var files = SavedFiles.All(cache);

        string[] expected =
        [
            "cache/FfxivVenues.json", "cache/InGamePartyFinder.json", "cache/Partake.json", "cache/RpEventCalendar.json",
            "cache/Xivpf.json", ListingCache.FetchTimesFile, ListingCache.MarksFile,
        ];
        Assert.Equal(expected, files.Keys.Order(StringComparer.Ordinal));
        Assert.All(files.Values, json => Assert.Equal(1, JsonDocument.Parse(json).RootElement.GetProperty("version").GetInt32()));
    }

    [Fact]
    public void OnlyTheMarksFileKeepsABackup()
    {
        var cache = new ListingCache();
        cache.ChangedAll();

        var backups = cache.TakeChanges().ToDictionary(file => file.Path, file => file.BackupPath);

        Assert.Equal(ListingCache.MarksBackupFile, backups[ListingCache.MarksFile]);
        Assert.Single(backups.Values, backup => backup is not null);
    }

    [Fact]
    public void DerivedValuesAreNotSaved()
    {
        var cache = new ListingCache();
        var ad = cache.SeenAd(ListingSource.InGamePartyFinder, "1", "Moonlit Lounge  Mist W14 P33", "Cuchulainn", Now);
        ad.Hidden = true;
        cache.Seen(ListingSource.RpEventCalendar, "2", "[M] Midnight Market", Now);

        var files = SavedFiles.All(cache);

        var all = string.Concat(files.Values);
        Assert.DoesNotContain("isMarked", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shownTitle", all, StringComparison.OrdinalIgnoreCase);
        // The ad's title is worked out from its text, and the RP event's shown title from its name
        Assert.DoesNotContain("\"title\"", files["cache/InGamePartyFinder.json"], StringComparison.Ordinal);
        Assert.DoesNotContain("\"Midnight Market\"", all, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingCountsAsNoChange()
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.Xivpf, "1", "Ad", null, Now).Favourite = true;
        cache.Fetched(ListingSource.Xivpf, Now);
        cache.Seen(ListingSource.Partake, "9", "Gone", Now).Hidden = true; // ended, so not in Partake's file: marks only

        var loaded = SavedFiles.RoundTrip(cache);

        Assert.Empty(loaded.TakeChanges());
    }

    [Fact]
    public void ASecondSaveWithNoChangeWritesNothing()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Venue", Now);
        cache.TakeChanges();

        Assert.Empty(cache.TakeChanges());
    }

    [Fact]
    public void AnInGameAdWritesOnlyTheInGameFile()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Venue", Now);
        cache.TakeChanges();

        cache.SeenAd(ListingSource.InGamePartyFinder, "2", "Ad", null, Now);

        Assert.Equal(["cache/InGamePartyFinder.json"], SavedFiles.Written(cache).Keys);
    }

    [Fact]
    public void AnXivpfAdTheGameAlsoSentWritesBothFiles()
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.InGamePartyFinder, "5", "Ad", null, Now.AddHours(-1));
        cache.TakeChanges();

        Xivpf.Record(cache, [new Xivpf.Ad("5", "Ad", null)], Now);

        Assert.Equal(["cache/InGamePartyFinder.json", "cache/Xivpf.json"], SavedFiles.Written(cache).Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Now, SavedFiles.Load(SavedFiles.All(cache)).Find(ListingSource.InGamePartyFinder, "5")!.LastSeen);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void AnXivpfAdTheGameSentAsLateOrLaterWritesOnlyXivpf(int hours)
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.InGamePartyFinder, "5", "Ad", null, Now);
        cache.TakeChanges();

        Xivpf.Record(cache, [new Xivpf.Ad("5", "Ad", null)], Now.AddHours(hours));

        Assert.Equal(["cache/Xivpf.json"], SavedFiles.Written(cache).Keys);
    }

    [Fact]
    public void AFetchWritesOnlyTheFetchTimes()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Venue", Now).Favourite = true;
        cache.TakeChanges();

        cache.Fetched(ListingSource.FfxivVenues, Now, "Aether");

        Assert.Equal([ListingCache.FetchTimesFile], SavedFiles.Written(cache).Keys);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void MarkingWritesOnlyTheMarks(bool mark, bool written)
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.FfxivVenues, "1", "Venue", Now);
        cache.TakeChanges();

        listing.Favourite = mark;

        Assert.Equal(written ? [ListingCache.MarksFile] : [], SavedFiles.Written(cache).Keys);
    }

    [Theory]
    [InlineData("Renamed", false, true)]
    [InlineData("Venue", true, true)]
    [InlineData("Venue", false, false)]
    public void AMarkedListingsNewTitleOrOneOffFlagRewritesTheMarks(string title, bool oneOff, bool marksWritten)
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.RpEventCalendar, "1", "Venue", Now).Hidden = true;
        cache.Seen(ListingSource.RpEventCalendar, "2", "Unmarked", Now);
        cache.TakeChanges();

        cache.Seen(ListingSource.RpEventCalendar, "1", title, Now, oneOff);
        cache.Seen(ListingSource.RpEventCalendar, "2", "Unmarked, renamed", Now, oneOff: true);

        Assert.Equal(marksWritten, SavedFiles.Written(cache).ContainsKey(ListingCache.MarksFile));
    }

    [Theory]
    [InlineData("New text", true)]
    [InlineData("Old text", false)]
    public void AMarkedAdsNewTextRewritesTheMarks(string text, bool marksWritten)
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.InGamePartyFinder, "1", "Old text", null, Now).Visited = true;
        cache.TakeChanges();

        cache.SeenAd(ListingSource.InGamePartyFinder, "1", text, null, Now);

        Assert.Equal(marksWritten, SavedFiles.Written(cache).ContainsKey(ListingCache.MarksFile));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ExpiringAListingWritesItsSourceAndTheMarksIfItWasMarked(bool marked, bool marksWritten)
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddMonths(-2), oneOff: true).Hidden = marked;
        cache.Seen(ListingSource.FfxivVenues, "2", "Venue", Now);
        cache.TakeChanges();

        cache.RemoveExpired(Now);

        var written = SavedFiles.Written(cache);
        Assert.Equal(["cache/Partake.json"], written.Keys.Where(path => path.StartsWith("cache/", StringComparison.Ordinal)));
        Assert.Equal(marksWritten, written.ContainsKey(ListingCache.MarksFile));
    }

    [Fact]
    public void ExpiringTwoMarkedListingsWritesTheMarks()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.Partake, "1", "Gala", Now.AddMonths(-2), oneOff: true).Hidden = true;
        cache.Seen(ListingSource.Partake, "2", "Ball", Now.AddMonths(-2), oneOff: true).Favourite = true;
        cache.TakeChanges();

        cache.RemoveExpired(Now);

        Assert.Contains(ListingCache.MarksFile, SavedFiles.Written(cache).Keys);
    }

    [Fact]
    public void AMarkWhoseListingTheSourceFilesLackStillShowsInTheHiddenList()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Lost lounge\nBar", Now).Hidden = true;
        var files = SavedFiles.All(cache);
        files.Remove(ListingCache.SourceFile(ListingSource.FfxivVenues));

        var loaded = SavedFiles.Load(files);

        Assert.Equal(["Lost lounge"], loaded.HiddenListings().Select(l => l.ShownTitle));
        // Recurring and marked, so it doesn't expire, and it isn't written to the venues file
        Assert.Equal(0, loaded.RemoveExpired(Now.AddYears(1)));
        loaded.ChangedAll();
        Assert.DoesNotContain("Lost lounge", loaded.TakeChanges().Single(f => f.Path == "cache/FfxivVenues.json").Json(), StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkedOneOffWhoseListingTheSourceFilesLackExpires()
    {
        var cache = new ListingCache();
        var gala = cache.Seen(ListingSource.Partake, "1", "Gala", Now, oneOff: true);
        gala.StartsAt = Now;
        gala.EndsAt = Now.AddHours(3);
        gala.ListedOn = "Lich";
        gala.Favourite = true;
        var files = SavedFiles.All(cache);
        files.Remove(ListingCache.SourceFile(ListingSource.Partake));

        var loaded = SavedFiles.Load(files);

        Assert.True(loaded.Find(ListingSource.Partake, "1")!.OneOff);
        Assert.Equal(1, loaded.RemoveExpired(Now));
    }

    [Fact]
    public void TheMarksBackupIsReadWhenTheMarksFileIsUnreadable()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Lounge", Now).Hidden = true;
        var files = SavedFiles.All(cache);
        files[ListingCache.MarksBackupFile] = files[ListingCache.MarksFile];
        files[ListingCache.MarksFile] = "{ not marks";
        var unreadable = new List<string>();

        var loaded = ListingCache.Load(path => files.GetValueOrDefault(path), (path, _) => unreadable.Add(path));

        Assert.Equal([ListingCache.MarksFile], unreadable);
        Assert.True(loaded.Find(ListingSource.FfxivVenues, "1")!.Hidden);
    }

    [Fact]
    public void TheMarksBackupIsReadWhenTheMarksFileIsMissing()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Lounge", Now).Visited = true;
        var files = SavedFiles.All(cache);
        files[ListingCache.MarksBackupFile] = files[ListingCache.MarksFile];
        files.Remove(ListingCache.MarksFile);

        Assert.True(SavedFiles.Load(files).Find(ListingSource.FfxivVenues, "1")!.Visited);
    }

    [Fact]
    public void TheMarksBackupIsIgnoredWhenTheMarksFileIsReadable()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Lounge", Now).Visited = true;
        var files = SavedFiles.All(cache);
        files[ListingCache.MarksBackupFile] = """{"version":1,"marks":[{"source":"FfxivVenues","id":"1","hidden":true}]}""";

        var listing = SavedFiles.Load(files).Find(ListingSource.FfxivVenues, "1")!;

        Assert.Equal((true, false), (listing.Visited, listing.Hidden));
    }

    [Fact]
    public void AnUnreadableSourceFileLeavesOutOnlyThatSource()
    {
        var cache = new ListingCache();
        cache.Seen(ListingSource.FfxivVenues, "1", "Lounge", Now).Favourite = true;
        cache.Seen(ListingSource.RpEventCalendar, "2", "Market", Now);
        var files = SavedFiles.All(cache);
        files["cache/RpEventCalendar.json"] = """{"version":1,"listings":[{"lastSeen":"2026-11-14T20:00:00+00:00","sent":{"id":"3"}}, 7]}""";
        var unreadable = new List<string>();

        var loaded = ListingCache.Load(path => files.GetValueOrDefault(path), (path, _) => unreadable.Add(path));

        Assert.Equal(["cache/RpEventCalendar.json"], unreadable);
        Assert.Equal(["1"], loaded.Listings.Select(l => l.Id));
        Assert.True(loaded.Find(ListingSource.FfxivVenues, "1")!.Favourite);
    }

    [Theory]
    [InlineData(ListingCache.MarksFile)]
    [InlineData(ListingCache.FetchTimesFile)]
    [InlineData("cache/Xivpf.json")]
    public void AFileFromALaterVersionIsUnreadable(string path)
    {
        var unreadable = new List<string>();

        ListingCache.Load(p => p == path ? """{"version": 2}""" : null, (p, _) => unreadable.Add(p));

        Assert.Equal([path], unreadable);
    }

    [Fact]
    public void FetchTimesLoadBack()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.Xivpf, Now);
        cache.Fetched(ListingSource.RpEventCalendar, Now.AddHours(-2));

        var loaded = SavedFiles.RoundTrip(cache);

        Assert.Equal(Now, loaded.LastFetched(ListingSource.Xivpf));
        Assert.Equal(Now.AddHours(-2), loaded.LastFetched(ListingSource.RpEventCalendar));
        Assert.Null(loaded.LastFetched(ListingSource.Partake));
    }

    [Fact]
    public void EachDataCentreOfASourceFetchedByDataCentreHasItsOwnFetchTime()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.FfxivVenues, Now, "Aether");
        cache.Fetched(ListingSource.FfxivVenues, Now.AddHours(-9), "Chaos");

        Assert.False(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now, "Aether"));
        Assert.True(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now, "Chaos"));
        Assert.True(cache.ShouldFetch(ListingSource.FfxivVenues, 10, Now, "Light"));
        Assert.Null(cache.LastFetched(ListingSource.FfxivVenues));
    }

    [Fact]
    public void DataCentreFetchTimesLoadBack()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.FfxivVenues, Now, "Aether");
        cache.Fetched(ListingSource.FfxivVenues, Now.AddHours(-1), "Materia");

        var files = SavedFiles.Written(cache);
        var loaded = SavedFiles.Load(files);

        Assert.Contains("\"FfxivVenues/Aether\":", files[ListingCache.FetchTimesFile], StringComparison.Ordinal);
        Assert.Equal(Now, loaded.LastFetched(ListingSource.FfxivVenues, "Aether"));
        Assert.Equal(Now.AddHours(-1), loaded.LastFetched(ListingSource.FfxivVenues, "Materia"));
    }

    [Theory]
    [InlineData(ListingSource.FfxivVenues, null)]
    [InlineData(ListingSource.FfxivVenues, "")]
    [InlineData(ListingSource.Xivpf, "Aether")]
    public void AFetchMustNameADataCentreOnlyForASourceFetchedByDataCentre(ListingSource source, string? dataCentre) =>
        Assert.Throws<ArgumentException>(() => new ListingCache().Fetched(source, Now, dataCentre));

    [Fact]
    public void LoadingDropsFetchTimesWhoseDataCentreDoesntMatchHowTheSourceIsFetched()
    {
        const string json = """
            {"version": 1, "lastFetched": {
              "FfxivVenues": "2026-11-14T20:00:00+00:00",
              "FfxivVenues/Light": "2026-11-14T19:00:00+00:00",
              "Xivpf/Light": "2026-11-14T20:00:00+00:00",
              "Partake": "2026-11-14T18:00:00+00:00",
              "NotASource/Light": "2026-11-14T20:00:00+00:00"
            }}
            """;

        var loaded = SavedFiles.Load(new Dictionary<string, string> { [ListingCache.FetchTimesFile] = json });

        Assert.Null(loaded.LastFetched(ListingSource.FfxivVenues));
        Assert.Equal(Now.AddHours(-1), loaded.LastFetched(ListingSource.FfxivVenues, "Light"));
        Assert.Null(loaded.LastFetched(ListingSource.Xivpf));
        Assert.Null(loaded.LastFetched(ListingSource.Xivpf, "Light"));
        Assert.Equal(Now.AddHours(-2), loaded.LastFetched(ListingSource.Partake));
    }

    [Fact]
    public void SourcesAreSavedByName()
    {
        var cache = new ListingCache();
        cache.Fetched(ListingSource.Partake, Now);
        cache.Seen(ListingSource.RpEventCalendar, "1", "Market", Now).Hidden = true;

        var files = SavedFiles.Written(cache);

        Assert.Contains("\"Partake\":", files[ListingCache.FetchTimesFile], StringComparison.Ordinal);
        Assert.Contains("\"source\":\"RpEventCalendar\"", files[ListingCache.MarksFile], StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingDropsFetchTimesForUnknownOrNeverFetchedSources()
    {
        const string json = """
            {"version": 1, "lastFetched": {
              "Xivpf": "2026-11-14T20:00:00+00:00",
              "NotASource": "2026-11-14T20:00:00+00:00",
              "InGamePartyFinder": "2026-11-14T20:00:00+00:00",
              "7": "2026-11-14T20:00:00+00:00",
              "xivpf": "2026-11-13T20:00:00+00:00"
            }}
            """;

        var loaded = SavedFiles.Load(new Dictionary<string, string> { [ListingCache.FetchTimesFile] = json });

        Assert.Equal(Now, loaded.LastFetched(ListingSource.Xivpf));
        Assert.Null(loaded.LastFetched(ListingSource.InGamePartyFinder));
        Assert.Equal(0, loaded.Count);
    }

    [Fact]
    public void LoadingDropsMarksForUnknownSourcesOrWithoutAnId()
    {
        const string json = """
            {"marks": [
              {"source": "NotASource", "id": "1", "hidden": true},
              {"source": "2", "id": "1", "hidden": true},
              {"source": "FfxivVenues", "id": "", "hidden": true},
              null,
              {"source": "FfxivVenues", "id": "4", "title": null, "hidden": true}
            ]}
            """;

        var loaded = SavedFiles.Load(new Dictionary<string, string> { [ListingCache.MarksFile] = json });

        var listing = Assert.Single(loaded.Listings);
        Assert.Equal(("4", "", true), (listing.Id, listing.Title, listing.Hidden));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public void LoadingAMinimalFileGivesAnEmptyCache(string json)
    {
        var files = new Dictionary<string, string> { [ListingCache.MarksFile] = json, [ListingCache.FetchTimesFile] = json };
        foreach (var source in Enum.GetValues<ListingSource>())
            files[ListingCache.SourceFile(source)] = json;

        var loaded = SavedFiles.Load(files);

        Assert.Equal(0, loaded.Count);
        Assert.Null(loaded.LastFetched(ListingSource.Xivpf));
    }

    [Fact]
    public void LoadingNoFilesGivesAnEmptyCache() => Assert.Equal(0, SavedFiles.Load(new Dictionary<string, string>()).Count);

    [Fact]
    public void LoadingDropsListingsWithoutAnIdAndKeepsTheLastRepeat()
    {
        const string json = """
            {"version": 1, "listings": [
              {"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {"id": "", "description": "no id"}},
              {"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {"description": "missing id"}},
              {"lastSeen": "2026-11-14T20:00:00+00:00", "sent": null},
              null,
              {"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {"id": "5", "description": "first"}},
              {"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {"id": "5", "description": "second"}}
            ]}
            """;

        var loaded = SavedFiles.Load(new Dictionary<string, string> { ["cache/Xivpf.json"] = json });

        Assert.Equal(1, loaded.Count);
        Assert.Equal("second", loaded.Find(ListingSource.Xivpf, "5")!.Title);
    }

    [Theory]
    [InlineData(ListingSource.InGamePartyFinder, """{"id": "5"}""")]
    [InlineData(ListingSource.InGamePartyFinder, """{"id": "5", "description": null, "listedOn": null}""")]
    [InlineData(ListingSource.Xivpf, """{"id": "5"}""")]
    [InlineData(ListingSource.Xivpf, """{"id": "5", "description": null, "listedOn": null}""")]
    public void AMissingOrNullDescriptionLoadsAsEmpty(ListingSource source, string sent)
    {
        var json = $$"""{"listings": [{"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {{sent}}}]}""";

        var listing = SavedFiles.Load(new Dictionary<string, string> { [ListingCache.SourceFile(source)] = json }).Find(source, "5")!;

        Assert.Equal(("", "", null, Now), (listing.Description, listing.Title, listing.ListedOn, listing.LastSeen));
    }

    [Fact]
    public void AdTextAndListingWorldSurviveSaving()
    {
        var cache = new ListingCache();
        cache.SeenAd(ListingSource.InGamePartyFinder, "123", "Café  Siren Mist W5 P12", "Siren", Now);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.InGamePartyFinder, "123")!;

        Assert.Equal(("Café  Siren Mist W5 P12", "Siren", "Café Siren Mist W5 P12"), (back.Description, back.ListedOn, back.Title));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData(""", "linkTexts": null""", new string[0])]
    [InlineData(""", "linkTexts": [null, "discord.gg/x"]""", new[] { "discord.gg/x" })]
    public void LoadingKeepsTheLinkTextsThatArentMissing(string links, string[] expected)
    {
        var json = $$$"""
            {"listings": [{"lastSeen": "2026-11-14T20:00:00+00:00", "sent":
              {"id": "5", "startsAt": "2026-11-14T21:00:00+00:00", "endsAt": "2026-11-14T23:00:00+00:00"{{{links}}}}}]}
            """;

        var listing = SavedFiles.Load(new Dictionary<string, string> { ["cache/Partake.json"] = json }).Find(ListingSource.Partake, "5")!;

        Assert.Equal(expected, listing.LinkTexts);
        Assert.Equal(("", ""), (listing.Title, listing.ListedOn));
    }

    [Fact]
    public void AnUnmarkedEventWithoutAWorldKeepsItsTitleAndAnEmptyWorld()
    {
        var cache = new ListingCache();
        var gala = cache.Seen(ListingSource.Partake, "1", "Gala", Now, oneOff: true);
        gala.StartsAt = Now;
        gala.EndsAt = Now.AddHours(3);

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.Partake, "1")!;

        Assert.Equal(("Gala", ""), (back.Title, back.ListedOn));
    }

    [Fact]
    public void AnEventsLocationOpeningsAndValidationSurviveSaving()
    {
        var cache = new ListingCache();
        var listing = cache.Seen(ListingSource.RpEventCalendar, "e1", "Market", Now, oneOff: true);
        var opening = new ListingOpening(Now, Now.AddHours(3));
        listing.ListedOn = "Mateus";
        listing.Location = "North Shroud, Hyrstmill";
        listing.Openings = [opening];
        listing.LastValidated = new DateOnly(2026, 8, 30);
        listing.Rating = "Mature";
        listing.LinkTexts = ["https://example.com/market"];

        var back = SavedFiles.RoundTrip(cache).Find(ListingSource.RpEventCalendar, "e1")!;

        Assert.Equal(("Market", "Mateus", "Mature", true), (back.Title, back.ListedOn, back.Rating, back.OneOff));
        Assert.Equal("North Shroud, Hyrstmill", back.Location);
        Assert.Equal([opening], back.Openings);
        Assert.Equal(["https://example.com/market"], back.LinkTexts);
        Assert.Equal(new DateOnly(2026, 8, 30), back.LastValidated);
    }

    [Theory]
    [InlineData("""{"id": "5"}""")]
    [InlineData("""{"id": "5", "openings": null, "location": null, "lastValidated": null, "linkTexts": null}""")]
    public void AnEventSavedWithoutItsFieldsLoadsWithout(string sent)
    {
        var json = $$"""{"listings": [{"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {{sent}}}]}""";

        var listing = SavedFiles.Load(new Dictionary<string, string> { ["cache/RpEventCalendar.json"] = json }).Find(ListingSource.RpEventCalendar, "5")!;

        Assert.Equal((null, 0, 0, null), (listing.Location, listing.Openings.Count, listing.LinkTexts.Count, listing.LastValidated));
    }

    [Fact]
    public void LoadingDropsOpeningsThatDontEndAfterTheyStart()
    {
        const string json = """
            {"listings": [{"lastSeen": "2026-11-14T20:00:00+00:00", "sent": {"id": "5", "openings": [
              {"start": "2026-11-14T20:00:00+00:00", "end": "2026-11-14T23:00:00+00:00"},
              null,
              {"start": "2026-11-15T20:00:00+00:00", "end": "2026-11-15T20:00:00+00:00"},
              {"start": "2026-11-16T20:00:00+00:00", "end": "2026-11-16T19:00:00+00:00"}]}}]}
            """;

        var listing = SavedFiles.Load(new Dictionary<string, string> { ["cache/RpEventCalendar.json"] = json }).Find(ListingSource.RpEventCalendar, "5")!;

        Assert.Equal([new ListingOpening(Now, Now.AddHours(3))], listing.Openings);
    }
}
