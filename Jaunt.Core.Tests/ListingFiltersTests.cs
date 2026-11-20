using Jaunt.Core.Browser;
using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ListingFiltersTests
{
    private static readonly string[] Fields = ["Moonlit Jazz Night", "Smooth jazz and cocktails. Open mic from 21:00.", "Bars", "Live shows"];

    private static bool Searches(string search, params string[] fields) => SearchQuery.Parse(search).Matches(fields.Length > 0 ? fields : Fields);

    private static FilterEntry Entry(
        Interests interests = Interests.None, bool nsfw = false, bool favourite = false, bool visited = false, bool hidden = false) =>
        new(new Tagging(interests, nsfw), favourite, visited, hidden, Fields);

    private static bool Shows(BrowserFilters filters, FilterEntry entry) => ListingFilters.Shows(filters, SearchQuery.Parse(filters.Search), entry);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("jazz")]
    [InlineData("JAZZ cocktails")]
    [InlineData("moonlit live")]
    [InlineData("\"open mic\"")]
    [InlineData("\"Open MIC")]
    [InlineData("-karaoke")]
    [InlineData("-\"jazz brunch\"")]
    [InlineData("jazz -")]
    [InlineData("\"\" jazz")]
    [InlineData("-\"\"")]
    public void SearchesThatMatch(string search) => Assert.True(Searches(search));

    [Theory]
    [InlineData("karaoke")]
    [InlineData("jazz karaoke")]
    [InlineData("-jazz")]
    [InlineData("-JAZZ")]
    [InlineData("\"jazz night club\"")]
    [InlineData("-\"open mic\"")]
    [InlineData("-\"open")]
    [InlineData("cocktails -bars")]
    public void SearchesThatDoNotMatch(string search) => Assert.False(Searches(search));

    [Fact]
    public void APhraseMustBeInOneField()
    {
        Assert.False(Searches("\"night smooth\""));
        Assert.True(Searches("night smooth"));
    }

    [Fact]
    public void ParsingSplitsTermsPhrasesAndExclusions()
    {
        var query = SearchQuery.Parse("jazz  -club \"live music\" -\"open mic\" \"unclosed rest");

        Assert.Equal(["jazz", "live music", "unclosed rest"], query.Include);
        Assert.Equal(["club", "open mic"], query.Exclude);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("\"\"")]
    [InlineData("-\"  \"")]
    [InlineData(" \t ")]
    public void NothingToSearchForGivesNoTerms(string search)
    {
        var query = SearchQuery.Parse(search);

        Assert.Empty(query.Include);
        Assert.Empty(query.Exclude);
    }

    [Fact]
    public void TheTagsMenuNamesEveryInterest() =>
        Assert.Equal(
            ["Bars", "Cafes", "Clubs", "Live shows", "Games", "Gambling", "Roleplay", "Spa", "Art", "Photography", "Tarot", "Chill"],
            ListingFilters.LabelsOf(ListingFilters.AllInterests));

    [Fact]
    public void AQuoteInsideAWordIsPartOfIt() => Assert.Equal(["it's\"ok"], SearchQuery.Parse("it's\"ok").Include);

    private static bool SearchesFolded(string search, string field) => SearchQuery.Parse(search).Matches([SearchQuery.Fold(field)]);

    [Theory]
    [InlineData("okami", "Ōkami Lounge")]
    [InlineData("OKAMI", "ōkami lounge")]
    [InlineData("ōkami", "Okami Lounge")]
    [InlineData("cafe", "Café Lumière")]
    [InlineData("\"cafe lumiere\"", "Café Lumière")]
    [InlineData("CAFÉ", "the cafe")]
    [InlineData("cafe", "Café decomposed")]
    [InlineData("hauptstrasse", "Hauptstraße")]
    [InlineData("bjork", "Bjørk's Bar")]
    [InlineData("aether", "Æther Spa")]
    [InlineData("lodz", "Łódź")]
    [InlineData("が", "がらす")]
    [InlineData("が", "がらす")]
    public void SearchIgnoresLatinAccents(string search, string field) => Assert.True(SearchesFolded(search, field));

    [Theory]
    [InlineData("-cafe", "Café Lumière")]
    [InlineData("-ōkami", "Okami Lounge")]
    [InlineData("か", "がらす")]
    [InlineData("하", "한")]
    [InlineData("cafe", "Caff")]
    public void FoldingKeepsOtherLettersApart(string search, string field) => Assert.False(SearchesFolded(search, field));

    [Fact]
    public void ByDefaultOnlyNsfwAndHiddenAreLeftOut()
    {
        var filters = new BrowserFilters();

        Assert.True(Shows(filters, Entry(favourite: true, visited: true)));
        Assert.True(Shows(filters, Entry()));
        Assert.False(Shows(filters, Entry(nsfw: true)));
        Assert.False(Shows(filters, Entry(hidden: true)));
    }

    [Fact]
    public void WithoutSfwOnlyNsfwShows() => Assert.True(Shows(new BrowserFilters { SfwOnly = false }, Entry(nsfw: true)));

    [Fact]
    public void ShowHiddenListsHiddenEntriesToo()
    {
        var filters = new BrowserFilters { ShowHidden = true };

        Assert.True(Shows(filters, Entry(hidden: true)));
        Assert.True(Shows(filters, Entry()));
        Assert.False(Shows(filters, Entry(hidden: true, nsfw: true)));
    }

    [Fact]
    public void FavouritesListsOnlyFavourites()
    {
        var filters = new BrowserFilters { Favourites = true };

        Assert.True(Shows(filters, Entry(favourite: true)));
        Assert.False(Shows(filters, Entry()));
    }

    [Fact]
    public void HideVisitedLeavesOutVisited()
    {
        var filters = new BrowserFilters { HideVisited = true };

        Assert.False(Shows(filters, Entry(visited: true)));
        Assert.True(Shows(filters, Entry()));
    }

    [Fact]
    public void EveryTagPickedMustMatch()
    {
        var filters = new BrowserFilters { Tags = Interests.Bars | Interests.LiveShows };

        Assert.True(Shows(filters, Entry(Interests.Bars | Interests.LiveShows | Interests.Roleplay)));
        Assert.False(Shows(filters, Entry(Interests.Bars)));
        Assert.False(Shows(filters, Entry(Interests.LiveShows)));
        Assert.False(Shows(filters, Entry()));
    }

    [Fact]
    public void SearchNarrowsTheFilteredEntries()
    {
        Assert.True(Shows(new BrowserFilters { Search = "jazz" }, Entry()));
        Assert.False(Shows(new BrowserFilters { Search = "-jazz" }, Entry()));
    }

    [Fact]
    public void EveryInterestHasOneLabel()
    {
        var all = Enum.GetValues<Interests>().Where(i => i != Interests.None).ToList();

        Assert.Equal(all.Order(), ListingFilters.Labels.Select(l => l.Interest).Order());
        Assert.Equal(all.Aggregate(Interests.None, (a, i) => a | i), ListingFilters.AllInterests);
        Assert.Equal(ListingFilters.Labels.Count, ListingFilters.Labels.Select(l => l.Label).Distinct().Count());
    }

    [Fact]
    public void LabelsOfKeepsTheMenuOrder() =>
        Assert.Equal(["Bars", "Live shows", "Chill"], ListingFilters.LabelsOf(Interests.Chill | Interests.LiveShows | Interests.Bars));
}
