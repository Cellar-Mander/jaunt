using Jaunt.Core.Listings;

namespace Jaunt.Core.Tests;

public class ListingAboutTests
{
    private static string Text(string written, bool scrubHandles = false) => ListingAbout.Clean(written, scrubHandles)!.Text;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n\t ")]
    [InlineData("![](https://cdn.partake.gg/assets/a.webp)")]
    [InlineData("![](https://cdn.partake.gg/assets/a.webp)\n\nhttps://venue.carrd.co")]
    [InlineData("**  **\n---\n✦ ✦ ✦")]
    public void NothingButImagesLinksAndSymbolsIsNoDescription(string? written) =>
        Assert.Null(ListingAbout.Clean(written));

    [Fact]
    public void AnInviteOnItsOwnIsKeptWithoutText()
    {
        var about = ListingAbout.Clean("![](https://cdn.partake.gg/assets/a.webp)\n\n\n\nhttps://discord.gg/zwcmzTN6bE\n")!;

        Assert.Equal(("", false), (about.Text, about.Cut));
        Assert.Equal(["https://discord.gg/zwcmzTN6bE"], about.Invites);
    }

    [Theory]
    [InlineData("Welcome to the bar.", "Welcome to the bar.")]
    [InlineData("  Line one  \r\nLine two\rLine three ", "Line one\nLine two\nLine three")]
    [InlineData("Para one\n\n\n\n\nPara two", "Para one\n\nPara two")]
    [InlineData("\n\nPara one\n \n", "Para one")]
    [InlineData("Wide\u00A0\u00A0gap\t\tand  spaces", "Wide gap and spaces")]
    public void KeepsLinesAndOneBlankLineBetweenParagraphs(string written, string expected) =>
        Assert.Equal(expected, Text(written));

    [Theory]
    [InlineData("# Welcome", "Welcome")]
    [InlineData("###### Small", "Small")]
    [InlineData("####### Seven", "####### Seven")]
    [InlineData("#hashtag", "#hashtag")]
    [InlineData("> Quoted", "Quoted")]
    [InlineData("- One\n* Two\n+ Three", "• One\n• Two\n• Three")]
    [InlineData("Above\n---\n* * *\n___\nBelow", "Above\n\nBelow")] // a rule parts sections, so it leaves a blank line
    [InlineData("-- not a rule", "-- not a rule")]
    [InlineData("List\n- ", "List\n•")]
    [InlineData("List\n-", "List\n-")]
    public void TurnsBlockMarkdownIntoPlainLines(string written, string expected) =>
        Assert.Equal(expected, Text(written));

    [Theory]
    [InlineData("**Bold** and __underlined__ and ~~struck~~", "Bold and underlined and struck")]
    [InlineData("*italic* and _also_", "italic and also")]
    [InlineData("**__Venue Name:__**  The Oasis", "Venue Name: The Oasis")]
    [InlineData("*18+* only", "18+ only")]
    [InlineData("5 * 3 = 15", "5 * 3 = 15")]
    [InlineData("snake_case_name", "snake_case_name")]
    [InlineData("A ~ B", "A ~ B")]
    [InlineData(@"\*not emphasis\* and a \\ backslash", @"*not emphasis* and a \ backslash")]
    [InlineData(@"C:\Path", @"C:\Path")]
    [InlineData(@"Café \é", @"Café \é")]
    [InlineData(@"a \ b", @"a \ b")]
    [InlineData("x\\\u007F", "x\\\u007F")]
    [InlineData("a**b and x ** y", "ab and x y")]
    public void RemovesEmphasisButKeepsSymbolsThatArentMarkdown(string written, string expected) =>
        Assert.Equal(expected, Text(written));

    [Theory]
    [InlineData("Find us ![logo](https://a.example.com/l.png) here", "Find us here")]
    [InlineData("[Our site](https://venue.carrd.co) has more", "Our site has more")]
    [InlineData("[**Bold link**](https://venue.carrd.co)", "Bold link")]
    [InlineData("[![](https://a.example.com/l.png) Join](https://discord.gg/abc)", "Join")]
    [InlineData("[not a link] (see above)", "[not a link] (see above)")]
    [InlineData("[unclosed](https://a.example.com", "[unclosed](https://a.example.com")]
    [InlineData("[ ] unchecked", "[ ] unchecked")]
    [InlineData("Price [about 5", "Price [about 5")]
    [InlineData("See [this]", "See [this]")]
    [InlineData("[a)](https://venue.carrd.co)", "a)")]
    public void RemovesImagesAndKeepsLinkText(string written, string expected) =>
        Assert.Equal(expected, Text(written));

    [Theory]
    [InlineData("One<br>two", "One two")]
    [InlineData("<b>Bold</b> text", "Bold text")]
    [InlineData("<p align=\"center\">Centred</p>", "Centred")]
    [InlineData("See <https://venue.carrd.co>", "See https://venue.carrd.co")]
    [InlineData("We <3 you", "We <3 you")]
    [InlineData("a < b > c", "a < b > c")]
    [InlineData("<unclosed tag", "<unclosed tag")]
    public void RemovesHtmlTags(string written, string expected) =>
        Assert.Equal(expected, Text(written));

    [Fact]
    public void CollectsDiscordInvitesFromLinksAndTextEachOnce()
    {
        var about = ListingAbout.Clean("""
            [Join us](https://discord.gg/first) or discord.gg/second
            Our site: https://venue.carrd.co
            Again: https://discord.gg/first and discord.com/invite/third
            """);

        Assert.Equal(
            ["https://discord.gg/first", "https://discord.gg/second", "https://discord.com/invite/third"],
            about!.Invites);
    }

    [Fact]
    public void ATextWithNoInvitesHasNone() =>
        Assert.Empty(ListingAbout.Clean("Visit https://venue.carrd.co")!.Invites);

    [Theory]
    [InlineData("Contact: @coco.shev for bookings", "Contact: for bookings")]
    [InlineData("Ask Enhasa#1319 or @_mod", "Ask or")]
    [InlineData("(@host) runs it", "() runs it")]
    [InlineData("Mail me@example.com", "Mail me@example.com")]
    [InlineData("Doors @ 8pm, or @8pm", "Doors @ 8pm, or @8pm")]
    [InlineData("Ticket #12345 and Room#123", "Ticket #12345 and Room#123")]
    [InlineData("Ward 5 #1234", "Ward 5 #1234")]
    [InlineData("Contact: Enhasa#1319", "Contact:")]
    [InlineData("Room#12345 upstairs", "Room#12345 upstairs")]
    [InlineData("@host runs it", "runs it")]
    [InlineData("@_mod runs it", "runs it")]
    [InlineData("Find us @", "Find us @")]
    public void ScrubsDiscordHandlesWhenAsked(string written, string expected) =>
        Assert.Equal(expected, Text(written, scrubHandles: true));

    [Fact]
    public void KeepsHandlesUnlessAsked() =>
        Assert.Equal("Contact @coco.shev or Enhasa#1319", Text("Contact @coco.shev or Enhasa#1319"));

    [Fact]
    public void ALongHandleIsRemovedOnlyUpToTheLongestUsername()
    {
        var handle = new string('a', 40);

        Assert.Equal(new string('a', 8), Text("@" + handle, scrubHandles: true));
    }

    [Fact]
    public void ATextUpToTheLimitIsKeptWhole()
    {
        var written = new string('a', ListingAbout.MaxLength - 2) + " b";

        var about = ListingAbout.Clean(written)!;

        Assert.Equal((written, false), (about.Text, about.Cut));
    }

    [Fact]
    public void ALongerTextIsCutAtTheLastSpaceWithAnEllipsis()
    {
        var words = string.Join(" ", Enumerable.Repeat("word", 400)); // 1,999 characters

        var about = ListingAbout.Clean(words)!;

        Assert.True(about.Cut);
        Assert.EndsWith("word…", about.Text, StringComparison.Ordinal);
        Assert.True(about.Text.Length <= ListingAbout.MaxLength + 1);
        Assert.StartsWith(about.Text[..^1], words, StringComparison.Ordinal);
        Assert.True(about.Text.Length > ListingAbout.MaxLength - 6);
    }

    [Fact]
    public void ACutAtALineBreakDropsIt()
    {
        var written = new string('a', ListingAbout.MaxLength - 10) + "\n" + new string('b', 20);

        Assert.Equal(new string('a', ListingAbout.MaxLength - 10) + "…", Text(written));
    }

    [Fact]
    public void ASpaceAtTheNearestAllowedPointStillTakesTheCut()
    {
        var written = new string('a', 1000) + " " + new string('b', 600);

        Assert.Equal(new string('a', 1000) + "…", Text(written));
    }

    [Fact]
    public void ALongTextWithoutANearbySpaceIsCutMidWord()
    {
        var written = "start " + new string('a', 2000);

        var text = Text(written);

        Assert.Equal(ListingAbout.MaxLength + 1, text.Length);
        Assert.EndsWith("a…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ACutNeverSplitsAnEmoji()
    {
        var written = new string('a', ListingAbout.MaxLength - 1) + "😀" + new string('b', 10);

        Assert.Equal(new string('a', ListingAbout.MaxLength - 1) + "…", Text(written));
    }

    [Fact]
    public void InvitesAfterTheCutAreKept()
    {
        var written = string.Join(" ", Enumerable.Repeat("word", 400)) + " https://discord.gg/late";

        var about = ListingAbout.Clean(written)!;

        Assert.True(about.Cut);
        Assert.Equal(["https://discord.gg/late"], about.Invites);
    }

    [Fact]
    public void TheLimitCountsTheCleanedText()
    {
        // 2,000 characters as sent, mostly an image, but well under the limit once cleaned
        var written = $"![]({new string('x', 1900)}) Welcome";

        Assert.Equal(("Welcome", false), (ListingAbout.Clean(written)!.Text, ListingAbout.Clean(written)!.Cut));
    }
}
