using System.Globalization;
using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

/// <summary>Dates, day words and 24/7 in <see cref="OpeningTimes.Parse"/>.</summary>
public class OpeningTimesDayTests
{
    private static readonly DateOnly Seen = new(2026, 11, 5); // a Thursday

    private static string P(string text, DateOnly? seen = null) => OpeningTimesFormat.Describe(OpeningTimes.Parse(text, seen ?? Seen));

    [Theory]
    [InlineData("Sat 7/11 20ST", "2026-11-07 20:00- ST")] // day/month, nearer the day seen than July 11
    [InlineData("11/5 9PM-12AM EST", "2026-11-05 21:00-00:00 EST")] // month/day
    [InlineData("Saturday 07.11.", "2026-11-07")] // a date on its own
    [InlineData("7.11. 8pm", "2026-11-07 20:00-")]
    [InlineData("10.30", "2026-10-30")]
    [InlineData("10.30 pm", "22:30-")] // a dotted date before a spaced PM is a time
    [InlineData("11/5 pm", "2026-11-05")] // but a slash date stays a date
    [InlineData("Nov 5th 9PM", "2026-11-05 21:00-")]
    [InlineData("16th Nov 7PM AEDT", "2026-11-16 19:00- AEDT")]
    [InlineData("Nov. 8, 6-9PM", "2026-11-08 18:00-21:00")]
    [InlineData("Nov 16 8pm", "2026-11-16 20:00-")]
    [InlineData("1st Nov", "2026-11-01")] // an ordinal, not 1 ST
    [InlineData("1st Nov 8pm", "2026-11-01 20:00-")]
    [InlineData("2nd Nov 8pm", "2026-11-02 20:00-")]
    [InlineData("3rd Nov 8pm", "2026-11-03 20:00-")]
    [InlineData("7.1. 8pm", "2027-01-07 20:00-")] // one digit after the dot, but a dot after it
    [InlineData("13/11 8pm", "2026-11-13 20:00-")] // only day/month is a date
    [InlineData("11/13 8pm", "2026-11-13 20:00-")] // only month/day is
    [InlineData("11/5 until 11pm", "2026-11-05 -23:00")]
    [InlineData("open 24/7, DJs 8pm", "20:00- ; always")] // 24/7 is not July 24
    [InlineData("14 NOVEMBER 5pm", "2026-11-14 17:00-")]
    [InlineData("sept 3 5pm", "2026-09-03 17:00-")]
    [InlineData("FRIDAY TONIGHT | 11/6 9P EST", "2026-11-06 21:00- EST")] // a date beats a weekday and tonight
    [InlineData("(13.8,11.4)", "")] // map coordinates
    [InlineData("配当2.3.4.6.15倍", "")] // a run of numbers
    [InlineData("80s/90s/00 stuff", "")]
    [InlineData("50/50 raffle", "")]
    [InlineData("13/13 8pm", "20:00-")] // no such date
    [InlineData("Nov 31 8pm", "20:00-")]
    [InlineData("Novel 3 8pm", "20:00-")] // only the month's name or its first three letters
    [InlineData("Ma 3 8pm", "20:00-")]
    [InlineData("11/5/2026 8pm", "20:00-")] // a third part is not read
    [InlineData("11/5:30", "")]
    public void Dates(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("1/2 8pm", "2026-12-30", "2027-01-02 20:00-")] // across the new year
    [InlineData("12/30 8pm", "2027-01-02", "2026-12-30 20:00-")]
    [InlineData("2/29 8pm", "2027-01-10", "2028-02-29 20:00-")] // only a leap year has it
    [InlineData("1/5 8pm", "2026-03-04", "2026-01-05 20:00-")] // Jan 5 and May 1 are 58 days away: month/day wins the tie
    [InlineData("1/1 8pm", "2028-07-02", "2028-01-01 20:00-")] // 2028-01-01 and 2029-01-01 are 183 days away: the earlier wins
    public void DatesTakeTheNearestReading(string text, string seen, string expected) =>
        Assert.Equal(expected, P(text, DateOnly.Parse(seen, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("FRI 17 ST", "fri 17:00- ST")]
    [InlineData("Friday @ 16 ST", "fri 16:00- ST")]
    [InlineData("Sat. 8-10p est", "sat 20:00-22:00 EST")]
    [InlineData("MONDAYS 7:30PM ET", "mon 19:30- ET")]
    [InlineData("tues, 8pm", "tue 20:00-")]
    [InlineData("Goth Friday w DJs 11-3 EST", "23:00-03:00 EST")] // not next to the time: an event name
    [InlineData("Friday night vibes", "")] // a weekday with no time says nothing
    [InlineData("TONIGHT | DJs | 8PM - 12AM ET", "today 20:00-00:00 ET")] // tonight applies to the whole ad
    [InlineData("TOMORROW 6PM ST", "tomorrow 18:00- ST")]
    [InlineData("Today, 19:00", "today 19:00-")]
    [InlineData("tonite 8pm", "today 20:00-")]
    [InlineData("come by tonight", "today")]
    [InlineData("tonight and tomorrow 8pm", "today 20:00-")] // the first day word wins
    [InlineData("tomorrow and tonight 8pm", "tomorrow 20:00-")]
    [InlineData("tonight until 11pm", "today -23:00")]
    [InlineData("Sun , - 8pm", "sun 20:00-")] // up to two separators between a weekday and its time
    [InlineData("Sun , , , 8pm", "20:00-")]
    public void Days(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("本日23時~〆は未定", "today 23:00-")] // 本日 runs into the hour
    [InlineData("土曜日21-25時", "sat 21:00-01:00")]
    [InlineData("明日 20時", "tomorrow 20:00-")]
    [InlineData("今夜 20時", "today 20:00-")]
    [InlineData("月曜 20時", "mon 20:00-")]
    [InlineData("明日 本日 20時", "tomorrow 20:00-")] // the first day word wins
    [InlineData("本日 明日 20時", "today 20:00-")]
    public void JapaneseDays(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("mon", "mon")]
    [InlineData("monday", "mon")]
    [InlineData("tue", "tue")]
    [InlineData("tues", "tue")]
    [InlineData("tuesday", "tue")]
    [InlineData("wed", "wed")]
    [InlineData("wednesday", "wed")]
    [InlineData("thu", "thu")]
    [InlineData("thur", "thu")]
    [InlineData("thurs", "thu")]
    [InlineData("thursday", "thu")]
    [InlineData("fri", "fri")]
    [InlineData("friday", "fri")]
    [InlineData("sat", "sat")]
    [InlineData("saturday", "sat")]
    [InlineData("sun", "sun")]
    [InlineData("sunday", "sun")]
    [InlineData("Tuesdays", "tue")]
    [InlineData("日曜日", "sun")]
    [InlineData("火曜", "tue")]
    [InlineData("水曜", "wed")]
    [InlineData("木曜", "thu")]
    [InlineData("金曜", "fri")]
    [InlineData("土曜", "sat")]
    public void EveryWeekdaySpelling(string word, string day) => Assert.Equal($"{day} 20:00-", P($"{word} 8pm"));

    [Theory]
    [InlineData("jan", 1)]
    [InlineData("feb", 2)]
    [InlineData("mar", 3)]
    [InlineData("apr", 4)]
    [InlineData("may", 5)]
    [InlineData("jun", 6)]
    [InlineData("jul", 7)]
    [InlineData("aug", 8)]
    [InlineData("sep", 9)]
    [InlineData("oct", 10)]
    [InlineData("nov", 11)]
    [InlineData("dec", 12)]
    [InlineData("January", 1)]
    [InlineData("DECEMBER", 12)]
    public void EveryMonth(string word, int month)
    {
        var expected = new DateOnly(month >= 6 ? 2026 : 2027, month, 3);
        Assert.Equal($"{expected:yyyy-MM-dd} 20:00-", P($"{word} 3 8pm"));
    }

    [Theory]
    [InlineData("24/7", "always")]
    [InlineData("open 24/7", "always")]
    [InlineData("Always Open", "always")]
    [InlineData("24/70", "")]
    [InlineData("9pm-24/7", "21:00- ; always")] // 24/7 is not the end of a range
    public void Always(string text, string expected) => Assert.Equal(expected, P(text));
}
