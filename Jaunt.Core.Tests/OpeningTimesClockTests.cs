using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>Times, ranges, closing times and zones in <see cref="OpeningTimes.Parse"/>.</summary>
public class OpeningTimesClockTests
{
    private static string P(string text) => OpeningTimesFormat.Describe(OpeningTimes.Parse(text));

    [Theory]
    [InlineData("@ 9pm", "21:00-")]
    [InlineData("07.00pm ST", "19:00- ST")] // a dot between hour and minutes, with PM
    [InlineData("12am ET", "00:00- ET")]
    [InlineData("12pm", "12:00-")]
    [InlineData("17 ST", "17:00- ST")]
    [InlineData("9 ST", "21:00- ST")] // a bare hour is evening
    [InlineData("09:00 ST", "09:00- ST")] // unless written with a leading zero
    [InlineData("0:30 ST", "00:30- ST")]
    [InlineData("12 ST", "12:00- ST")] // a bare 12 is noon
    [InlineData("7:59 ST", "19:59- ST")]
    [InlineData("10.30 pm", "22:30-")] // a dotted time with a spaced PM
    [InlineData("10.30 am", "10:30-")]
    [InlineData("10.30pm", "22:30-")]
    [InlineData("8pm 9pm", "20:00- ; 21:00-")] // reading goes on straight after a time
    public void SingleTimes(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("6MST", "18:00- MST")]
    [InlineData("OPEN@12amET", "00:00- ET")] // AM and the zone joined
    [InlineData("10:30EST", "22:30- EST")]
    [InlineData("20ST", "20:00- ST")]
    [InlineData("9 AEDT", "21:00- AEDT")]
    [InlineData("9 aedt", "21:00- AEDT")] // zone upper-cased
    [InlineData("10pEST", "22:00- EST")]
    [InlineData("10pmEST", "22:00- EST")]
    [InlineData("10aPT", "10:00- PT")]
    [InlineData("10aEST", "22:00- AEST")] // AEST is a zone, so it wins over A then EST
    [InlineData("Fri 21st 9pm", "21:00-")] // only 9pm, not 21:00 ST
    [InlineData("21ST", "21:00- ST")] // upper case is server time
    [InlineData("2st", "14:00- ST")] // only 1 and 21 take "st" as ordinals
    [InlineData("1:30st", "13:30- ST")]
    public void ZonesAreReadAfterTheTime(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("W30 P52")] // ward and plot
    [InlineData("Gob 14-35")] // a bare range with no AM, PM, zone or minutes
    [InlineData("Raffle21m 10M Glam")]
    [InlineData("FC@2 thanks")] // party size
    [InlineData("W:27P:43")]
    [InlineData("discord.gg/7FYG4 prtk.gg/56485")]
    [InlineData("10.3")] // a dot needs AM or PM
    [InlineData("25 ST")] // not an hour
    [InlineData("13pm")]
    [InlineData("7:60 ST")] // not minutes; the bare 7 is then no time either
    [InlineData("8 to 11")]
    public void NumbersThatAreNotTimes(string text) => Assert.Equal("", P(text));

    [Theory]
    [InlineData("ST")]
    [InlineData("UTC")]
    [InlineData("GMT")]
    [InlineData("BST")]
    [InlineData("CET")]
    [InlineData("CEST")]
    [InlineData("ET")]
    [InlineData("EST")]
    [InlineData("EDT")]
    [InlineData("CT")]
    [InlineData("CST")]
    [InlineData("CDT")]
    [InlineData("MT")]
    [InlineData("MST")]
    [InlineData("MDT")]
    [InlineData("PT")]
    [InlineData("PST")]
    [InlineData("PDT")]
    [InlineData("AET")]
    [InlineData("AEST")]
    [InlineData("AEDT")]
    [InlineData("ACST")]
    [InlineData("ACDT")]
    [InlineData("AWST")]
    [InlineData("NZST")]
    [InlineData("NZDT")]
    [InlineData("JST")]
    public void EveryZoneIsRead(string zone) => Assert.Equal($"20:00- {zone}", P($"8pm {zone.ToLowerInvariant()}"));

    [Theory]
    [InlineData("20時", "20:00-")]
    [InlineData("31時", "")] // beyond 30 is not an hour
    [InlineData("4・8 PM", "20:00-")] // the katakana middle dot is not Japanese text, so 8 PM is evening
    public void JapaneseForms(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData('぀')] // first of hiragana
    [InlineData('ヺ')] // last katakana letter before the middle dot
    [InlineData('ー')]
    [InlineData('ヽ')]
    [InlineData('ヾ')]
    [InlineData('ヿ')]
    [InlineData('㐀')] // CJK extension A
    [InlineData('䶿')]
    [InlineData('一')] // CJK unified ideographs
    [InlineData('鿿')]
    [InlineData('豈')] // compatibility ideographs
    [InlineData('﫿')]
    [InlineData('々')]
    [InlineData('〆')]
    public void JapaneseCharactersMeanTheTwentyFourHourClock(char c) => Assert.Equal("09:00- ST", P($"{c} 9 ST"));

    [Theory]
    [InlineData('〿')]
    [InlineData('・')]
    [InlineData('㏿')]
    [InlineData('䷀')]
    [InlineData('ꀀ')]
    [InlineData('')]
    [InlineData('ﬀ')]
    public void OtherCharactersDoNot(char c) => Assert.Equal("21:00- ST", P($"{c} 9 ST"));
}
