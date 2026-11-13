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
    [InlineData("喫茶 7:00~9:00", "07:00-09:00")] // Japanese text is on the 24-hour clock
    [InlineData("喫茶 9-11 ST", "09:00-11:00 ST")]
    [InlineData("21-30時", "21:00-06:00")]
    [InlineData("ゴブ27-10 16時close", "-16:00")]
    [InlineData("ラベ19-5 18時〆", "-18:00")]
    [InlineData("4時〆", "-04:00")]
    [InlineData("〆14:00", "-14:00")]
    [InlineData("~17:00〆", "-17:00")]
    [InlineData("23時くらいまで", "-23:00")]
    [InlineData("18時まで", "-18:00")]
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

    [Theory]
    [InlineData("7-11 PM EST", "19:00-23:00 EST")] // only the end says PM
    [InlineData("2 - 6a", "02:00-06:00")] // only the end says AM
    [InlineData("11-3 EST", "23:00-03:00 EST")] // neither: evening, then the end within 12 hours
    [InlineData("8-12 ST", "20:00-00:00 ST")]
    [InlineData("9:30-12:30 ET", "21:30-00:30 ET")]
    [InlineData("8:30-11:00pm EST", "20:30-23:00 EST")]
    [InlineData("9pm-12", "21:00-00:00")] // only the start says PM
    [InlineData("12-4pm", "12:00-16:00")]
    [InlineData("12p-8p EST", "12:00-20:00 EST")]
    [InlineData("10p-3a EST", "22:00-03:00 EST")]
    [InlineData("11 pm - 4 am EDT", "23:00-04:00 EDT")]
    [InlineData("8PM EST to 11PM EST", "20:00-23:00 EST")]
    [InlineData("FROM 11PM TIL 3AM EST", "23:00-03:00 EST")]
    [InlineData("7–10 PM PT", "19:00-22:00 PT")] // en dash
    [InlineData("7—10 PM PT", "19:00-22:00 PT")] // em dash
    [InlineData("9pm--2am", "21:00-02:00")] // more than one dash
    [InlineData("9pm till 2am", "21:00-02:00")]
    [InlineData("9pm until 2am", "21:00-02:00")]
    [InlineData("8PM PST to 11PM EST", "20:00-23:00 EST")] // the end's zone wins
    [InlineData("17-2 ST", "17:00-02:00 ST")] // 24-hour start
    [InlineData("1pm-2am", "13:00-02:00")] // AM on the end wins over the nearer reading, 14:00
    [InlineData("10amEST-2pm", "10:00-14:00 EST")] // a joined AM and zone, then the range goes on
    [InlineData("9pm til 2am", "21:00-02:00")]
    [InlineData("8-8pm", "08:00-20:00")] // the same reading for both is a whole day, so the other wins
    [InlineData("10pm-1amEST", "22:00-01:00 EST")]
    [InlineData("8-11PST", "20:00-23:00 PST")] // PST is a zone, not P then ST
    [InlineData("8-10p est", "20:00-22:00 EST")]
    public void RangesFillInAmAndPm(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("until 11pm", "-23:00")]
    [InlineData("till 11pm", "-23:00")]
    [InlineData("til 2am", "-02:00")]
    [InlineData("--2am EST", "-02:00 EST")]
    [InlineData("-2am", "02:00-")] // one dash is not "until"
    [InlineData("close 7.45", "")] // a dot without AM or PM is not minutes, and nothing after the hour is read
    public void ClosingTimes(string text, string expected) => Assert.Equal(expected, P(text));
}
