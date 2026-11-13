using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>"Open now" in <see cref="OpeningTimes.Parse"/>.</summary>
public class OpeningTimesNowTests
{
    private static string P(string text) => OpeningTimesFormat.Describe(OpeningTimes.Parse(text, new DateOnly(2026, 11, 5)));

    [Theory]
    [InlineData("OPEN NOW", "now")]
    [InlineData("now open", "now")]
    [InlineData("TWIN SINS is open!", "now")]
    [InlineData("SHADOWSONG OPEN 100M", "now")]
    [InlineData("is open until 1a EST", "now -01:00 EST")] // with a closing time
    [InlineData("open now till 11pm ET", "now -23:00 ET")]
    [InlineData("is open til 2am", "now -02:00")]
    [InlineData("営業中 18時まで", "now -18:00")]
    [InlineData("NOW OPEN | 7 PM ST", "19:00- ST ; now")] // a start time is its own opening
    [InlineData("is Open 12p-8p EST", "12:00-20:00 EST ; now")]
    [InlineData("Open 6PM PT", "18:00- PT")] // a time after open says when it opens
    [InlineData("OPEN@12amET", "00:00- ET")]
    [InlineData("20:00 ST Open.", "20:00- ST")]
    [InlineData("8-11pm Open.", "20:00-23:00")]
    [InlineData("--2am Open", "-02:00")] // a time right before open, even a closing one, isn't "open now"
    [InlineData("is open from 8PM EST", "20:00- EST")]
    [InlineData("is open TONIGHT @ 9PM CST", "today 21:00- CST")]
    [InlineData("Open Tonight!", "today")]
    [InlineData("open tonite", "today")]
    [InlineData("open today", "today")]
    [InlineData("open tomorrow", "tomorrow")]
    [InlineData("Open Stage Open Bar Open Mic", "")]
    [InlineData("Open Recruitment, open to all", "")]
    [InlineData("the full experience starts now", "")]
    [InlineData("doors start now", "")]
    [InlineData("is now hiring", "")]
    [InlineData("open:24/7", "always")]
    [InlineData("24/7 Open Bar, OPEN NOW!", "always ; now")]
    [InlineData("open 24 hours", "")]
    [InlineData("リニューアルオープンに向け", "")]
    [InlineData("opening night", "")]
    public void NowAndAlways(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("open stage")]
    [InlineData("open bar")]
    [InlineData("open mic")]
    [InlineData("open recruitment")]
    [InlineData("open recruiting")]
    [InlineData("open positions")]
    [InlineData("open applications")]
    [InlineData("open auditions")]
    [InlineData("open at Bryn")]
    [InlineData("open to all")]
    [InlineData("now hiring")]
    [InlineData("now recruiting")]
    [InlineData("now accepting")]
    public void WordsThatSayWhatIsOpen(string text) => Assert.Equal("", P(text));

    [Theory]
    [InlineData("営業中")]
    [InlineData("開店中")]
    [InlineData("オープン中")]
    [InlineData("オープンです")]
    [InlineData("開店しております")]
    [InlineData("ただいま枠有り")]
    public void JapaneseOpenNow(string text) => Assert.Equal("now", P(text));
}
