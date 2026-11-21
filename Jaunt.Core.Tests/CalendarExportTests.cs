using System.Text;
using Jaunt.Core.Browser;

namespace Jaunt.Core.Tests;

public class CalendarExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    // 21:00 to 01:00 in New York (UTC-5), so the UTC times cross midnight
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 21, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset End = new(2026, 11, 23, 1, 0, 0, TimeSpan.FromHours(-5));

    private static CalendarEvent Event(
        string title = "The Lotus Lounge", DateTimeOffset? end = null, string? location = "Balmung · Mist · Ward 5 · Plot 12",
        string? description = "Drinks and dancing", Uri? url = null) =>
        new("uid-1@jaunt", title, Start, end ?? End, location, description, url);

    private static string[] Lines(string ics) => ics.Split("\r\n");

    // RFC 5545 §3.1: a CRLF followed by one space or tab is removed when reading
    private static string[] Unfolded(string ics) => Lines(ics.Replace("\r\n ", "", StringComparison.Ordinal));

    private static string Property(string ics, string name) =>
        Assert.Single(Unfolded(ics), l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public void IcsHasOneEventInUtcWithCrlfLineEnds()
    {
        var ics = CalendarExport.Ics(Event(), Now);

        Assert.StartsWith(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Jaunt//Jaunt//EN\r\nCALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\nBEGIN:VEVENT\r\nUID:",
            ics, StringComparison.Ordinal);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", ics, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', ics.Replace("\r\n", "", StringComparison.Ordinal));
        Assert.Equal("20261123T020000Z", Property(ics, "DTSTART"));
        Assert.Equal("20261123T060000Z", Property(ics, "DTEND"));
        Assert.Equal("20261122T120000Z", Property(ics, "DTSTAMP"));
        Assert.Equal("uid-1@jaunt", Property(ics, "UID"));
        Assert.Equal("The Lotus Lounge", Property(ics, "SUMMARY"));
        Assert.Equal("Balmung · Mist · Ward 5 · Plot 12", Property(ics, "LOCATION"));
        Assert.Equal("Drinks and dancing", Property(ics, "DESCRIPTION"));
    }

    [Fact]
    public void IcsEscapesTextSpecials()
    {
        var ics = CalendarExport.Ics(Event(title: @"Wine; Song, and \ more", description: "Line one\r\nLine two\rLine three\nend"), Now);

        Assert.Equal(@"Wine\; Song\, and \\ more", Property(ics, "SUMMARY"));
        Assert.Equal(@"Line one\nLine two\nLine three\nend", Property(ics, "DESCRIPTION"));
        // A CR as the last character has no LF after it to look at
        Assert.Equal(@"Last\nline", Property(CalendarExport.Ics(Event(description: "Last\rline\r"), Now), "DESCRIPTION"));
    }

    [Fact]
    public void IcsDropsControlCharactersSoTextCantAddProperties()
    {
        var ics = CalendarExport.Ics(Event(title: "Club\u0000\u001b\u0085 Night\u000bEND:VEVENT"), Now);

        Assert.Equal("Club NightEND:VEVENT", Property(ics, "SUMMARY"));
        Assert.Single(Lines(ics), l => l == "END:VEVENT");
    }

    [Fact]
    public void IcsFoldsLongLinesAt75OctetsWithoutSplittingACharacter()
    {
        // 3-octet kana and a 4-octet emoji make the fold points fall inside characters if counted wrongly
        var title = string.Concat(Enumerable.Repeat("あ😀b", 40));
        var ics = CalendarExport.Ics(Event(title: title), Now);

        Assert.All(Lines(ics), l => Assert.InRange(Encoding.UTF8.GetByteCount(l), 0, 75));
        Assert.Contains(Lines(ics), l => l.StartsWith(' ') && Encoding.UTF8.GetByteCount(l) > 70);
        Assert.DoesNotContain(Lines(ics), l => l.Length > 1 && (char.IsHighSurrogate(l[^1]) || (l[0] == ' ' && char.IsLowSurrogate(l[1]))));
        Assert.Equal(title, Property(ics, "SUMMARY"));
    }

    [Fact]
    public void ALoneHighSurrogateAtTheEndOfALineIsWrittenAsIs()
    {
        // Only the UID isn't cut, which would drop it; there is no character after it to pair with
        var ics = CalendarExport.Ics(Event() with { Uid = "uid\uD83D" }, Now);

        Assert.Equal("uid\uD83D", Property(ics, "UID"));
    }

    [Fact]
    public void ALineOfExactly75OctetsIsNotFolded()
    {
        // "SUMMARY:" is 8 octets
        var title = new string('x', 67);
        var ics = CalendarExport.Ics(Event(title: title), Now);

        Assert.Contains("SUMMARY:" + title, Lines(ics));
        Assert.Equal(title + "y", Property(CalendarExport.Ics(Event(title: title + "y"), Now), "SUMMARY"));
        Assert.Contains(" y", Lines(CalendarExport.Ics(Event(title: title + "y"), Now)));
    }

    [Fact]
    public void WithNoClosingTimeIcsLeavesOutDtendAndLinksEndAtTheStart()
    {
        var noEnd = Event() with { End = null };
        var backwards = Event() with { End = Start.AddHours(-1) };
        var same = Event() with { End = Start };

        Assert.DoesNotContain(Unfolded(CalendarExport.Ics(noEnd, Now)), l => l.StartsWith("DTEND", StringComparison.Ordinal));
        Assert.DoesNotContain(Unfolded(CalendarExport.Ics(backwards, Now)), l => l.StartsWith("DTEND", StringComparison.Ordinal));
        Assert.DoesNotContain(Unfolded(CalendarExport.Ics(same, Now)), l => l.StartsWith("DTEND", StringComparison.Ordinal));
        Assert.Contains("dates=20261123T020000Z%2F20261123T020000Z", CalendarExport.GoogleLink(noEnd).AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("enddt=2026-11-23T02%3A00%3A00Z", CalendarExport.OutlookLink(backwards).AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyFieldsAreLeftOutAndAnEmptyTitleIsUntitled()
    {
        var bare = Event(title: "  ", location: null, description: "") with { End = null };
        var ics = CalendarExport.Ics(bare, Now);

        Assert.Equal("(untitled)", Property(ics, "SUMMARY"));
        Assert.DoesNotContain(Unfolded(ics), l => l.StartsWith("LOCATION", StringComparison.Ordinal) || l.StartsWith("DESCRIPTION", StringComparison.Ordinal) || l.StartsWith("URL", StringComparison.Ordinal));
        Assert.DoesNotContain(Unfolded(CalendarExport.Ics(bare with { Description = null }, Now)), l => l.StartsWith("DESCRIPTION", StringComparison.Ordinal));
        var google = CalendarExport.GoogleLink(bare).AbsoluteUri;
        Assert.DoesNotContain("location=", google, StringComparison.Ordinal);
        Assert.DoesNotContain("details=", google, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageLinkIsTheUrlAndLeadsTheDescription()
    {
        var withUrl = Event(url: new Uri("https://lotus.example/menu?a=1,2"));
        var ics = CalendarExport.Ics(withUrl, Now);

        Assert.Equal("https://lotus.example/menu?a=1,2", Property(ics, "URL"));
        Assert.Equal(@"https://lotus.example/menu?a=1\,2\n\nDrinks and dancing", Property(ics, "DESCRIPTION"));
        Assert.Equal("https://lotus.example/menu?a=1\\,2", Property(CalendarExport.Ics(withUrl with { Description = null }, Now), "DESCRIPTION"));
        Assert.Equal("https://lotus.example/menu?a=1\\,2", Property(CalendarExport.Ics(withUrl with { Description = "" }, Now), "DESCRIPTION"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    public void OnlyWebLinksAreExported(string url)
    {
        var ics = CalendarExport.Ics(Event(url: new Uri(url)), Now);

        Assert.DoesNotContain(Unfolded(ics), l => l.StartsWith("URL", StringComparison.Ordinal));
        Assert.Equal("Drinks and dancing", Property(ics, "DESCRIPTION"));
    }

    [Fact]
    public void LongTextIsCutToItsCapWithoutSplittingASurrogatePair()
    {
        var description = new string('a', CalendarExport.MaxDescription - 1) + "😀tail";
        var ics = CalendarExport.Ics(Event(title: new string('t', 500), description: description), Now);

        Assert.Equal(new string('t', CalendarExport.MaxTitle), Property(ics, "SUMMARY"));
        Assert.Equal(new string('a', CalendarExport.MaxDescription - 1), Property(ics, "DESCRIPTION"));
        var google = Uri.UnescapeDataString(CalendarExport.GoogleLink(Event(description: description)).Query);
        Assert.Contains("details=" + new string('a', CalendarExport.MaxLinkDescription) + "&", google + "&", StringComparison.Ordinal);
    }

    [Fact]
    public void GoogleLinkFillsInTheNewEventPage()
    {
        var link = CalendarExport.GoogleLink(Event(title: "Lotus & Co #1", url: new Uri("https://lotus.example/")));

        Assert.Equal(
            "https://calendar.google.com/calendar/render?action=TEMPLATE&text=Lotus%20%26%20Co%20%231"
            + "&dates=20261123T020000Z%2F20261123T060000Z"
            + "&location=Balmung%20%C2%B7%20Mist%20%C2%B7%20Ward%205%20%C2%B7%20Plot%2012"
            + "&details=https%3A%2F%2Flotus.example%2F%0A%0ADrinks%20and%20dancing",
            link.AbsoluteUri);
    }

    [Fact]
    public void OutlookLinkFillsInTheNewEventPage()
    {
        var link = CalendarExport.OutlookLink(Event());

        Assert.Equal(
            "https://outlook.live.com/calendar/0/action/compose?rru=addevent&subject=The%20Lotus%20Lounge"
            + "&startdt=2026-11-23T02%3A00%3A00Z&enddt=2026-11-23T06%3A00%3A00Z"
            + "&location=Balmung%20%C2%B7%20Mist%20%C2%B7%20Ward%205%20%C2%B7%20Plot%2012"
            + "&body=Drinks%20and%20dancing",
            link.AbsoluteUri);
    }

    [Fact]
    public void TheExportableOpeningIsTheFirstListedWithAStart()
    {
        var openNow = new Occurrence(null, Start);
        var next = new Occurrence(Start, End);
        var later = new Occurrence(End, null);

        Assert.Same(next, CalendarExport.Exportable([openNow, next, later], alwaysOpen: false));
        Assert.Same(later, CalendarExport.Exportable([later, next], alwaysOpen: false));
        Assert.Null(CalendarExport.Exportable([openNow], alwaysOpen: false));
        Assert.Null(CalendarExport.Exportable([], alwaysOpen: false));
        Assert.Null(CalendarExport.Exportable([next], alwaysOpen: true));
    }

    [Fact]
    public void UidsAndFileNamesAreStablePerOpeningAndSafe()
    {
        var uid = CalendarExport.UidFor("Partake", "../../evil\\name", Start);

        Assert.Equal(uid, CalendarExport.UidFor("Partake", "../../evil\\name", Start));
        Assert.NotEqual(uid, CalendarExport.UidFor("Partake", "../../evil\\name", End));
        Assert.NotEqual(uid, CalendarExport.UidFor("Xivpf", "../../evil\\name", Start));
        Assert.Matches("^[0-9a-f]{16}-20261123T020000Z@jaunt$", uid);
        Assert.Matches("^jaunt-[0-9a-f]{16}\\.ics$", CalendarExport.FileName(Event() with { Uid = uid }));
        Assert.NotEqual(CalendarExport.FileName(Event() with { Uid = uid }), CalendarExport.FileName(Event()));
    }
}
