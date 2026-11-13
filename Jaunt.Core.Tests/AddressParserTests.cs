using Jaunt.Core.Text;

namespace Jaunt.Core.Tests;

public class AddressParserTests
{
    /// <summary>
    /// Each address as "World[?] District W.P extras", where ? marks a world taken from the listing.
    /// Joined by " ; ", or "-" when there is none.
    /// </summary>
    private static string P(string text, string? listedOn = "Lich")
    {
        var addresses = TestGazetteer.Parser.Parse(text, listedOn);
        return addresses.Count == 0 ? "-" : string.Join(" ; ", addresses.Select(Short));
    }

    private static string Short(HousingAddress a)
    {
        var parts = new List<string>
        {
            (a.World ?? "null") + (a.WorldFrom == WorldSource.Listing ? "?" : ""),
            a.District?.ToString() ?? "null",
            $"{a.Ward}.{a.Plot?.ToString() ?? "_"}",
        };
        if (a.Subdivision is { } s)
            parts.Add(s ? "sub" : "main");
        if (a.Apartment is { } apt)
            parts.Add($"apt{apt}");
        if (a.Room is { } room)
            parts.Add($"room{room}");
        return string.Join(' ', parts);
    }

    [Theory]
    [InlineData("Lich LB W20 P28", "Lich LavenderBeds 20.28")]
    [InlineData("Odin /Goblet /Ward27 /Plot36", "Odin Goblet 27.36")]
    [InlineData("Alpha Emp W:27P:43", "Alpha Empyreum 27.43")]
    [InlineData("Alpha Emp W 22 - P 37", "Alpha Empyreum 22.37")]
    [InlineData("ODIN/EMP/W2/30", "Odin Empyreum 2.30")] // plot without a P
    [InlineData("Mat| Gob| W9|34", "Mateus Goblet 9.34")]
    [InlineData("Omega Goblet W6 P29/30", "Omega Goblet 6.29 ; Omega Goblet 6.30")]
    public void WardThenPlot(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Alpha Mist W2 30", "Alpha Mist 2._")] // a bare plot needs / or |
    [InlineData("Alpha Mist W2-30", "Alpha Mist 2._")]
    [InlineData("Alpha Mist W2/30pm", "Alpha Mist 2._")] // a time, not a plot
    [InlineData("Alpha Mist W2 P3 / 4", "Alpha Mist 2.3 ; Alpha Mist 2.4")]
    [InlineData("Alpha Mist W2 P3 - 4", "Alpha Mist 2.3")] // only / joins a second plot
    public void BarePlotsNeedTheirSeparator(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Phoenix at Lavender Beds, Plot 40, Ward 5", "Phoenix LavenderBeds 5.40")]
    [InlineData("Bryn, LB, P51 W11", "Brynhildr LavenderBeds 11.51")]
    [InlineData("Mist 5th Ward Plot 3", "Lich? Mist 5.3")]
    [InlineData("Mist 5 Ward Plot 3", "-")] // an ordinal needs its suffix
    [InlineData("Mist 5thWard Plot 3", "Lich? Mist 5.3")]
    public void PlotThenWardAndOrdinals(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Lich Mist W30 P60", "Lich Mist 30.60")]
    [InlineData("Lich Mist W1 P1", "Lich Mist 1.1")]
    [InlineData("Lich Mist W31 P2", "-")]
    [InlineData("Lich Mist W0 P2", "-")]
    [InlineData("Lich Mist W5 P61", "Lich Mist 5._")] // the plot is dropped, the ward stays
    [InlineData("Lich Mist 31-2", "-")]
    [InlineData("Lich Mist 5-61", "-")]
    [InlineData("Lich Mist 5-0", "-")]
    [InlineData("Lich Mist W5 P1234567", "Lich Mist 5._")] // too long to be a number at all
    public void WardsAndPlotsMustBeInRange(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Raiden-LB-3-28", "Raiden LavenderBeds 3.28")]
    [InlineData("Alpha Empy 6 12", "Alpha Empyreum 6.12")]
    [InlineData("Mana/Chocobo/Mist/17-17", "Chocobo Mist 17.17")]
    [InlineData("AFK CLUB BalGob28 P5", "Balmung Goblet 28.5")]
    [InlineData("Alpha Mist 11/6", "-")] // a date
    [InlineData("Alpha Mist 8-11pm", "-")] // a time
    [InlineData("Alpha Mist 8-11 EST", "-")]
    [InlineData("Alpha Mist 8-11:30", "-")]
    [InlineData("Alpha Mist 8", "-")]
    [InlineData("Alpha Mist W8", "Alpha Mist 8._")]
    public void DistrictThenBareNumbers(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Gaia/Ifrit/6-51", "Ifrit null 6.51")]
    [InlineData("Ifrit 6 51", "-")] // after a world, only a dash
    [InlineData("Ifrit 6-11pm", "-")]
    public void WorldThenBareNumbers(string text, string expected) => Assert.Equal(expected, P(text, "Anima"));

    [Theory]
    [InlineData("Animaラベ21区16番地", "Anima LavenderBeds 21.16")]
    [InlineData("ゆに鯖ゴブ第23区32番地", "Unicorn Goblet 23.32")]
    [InlineData("メテオ/ベリアス/ラベ21区13", "Belias LavenderBeds 21.13")]
    [InlineData("マナパンデモゴブ27-10", "Pandaemonium Goblet 27.10")] // no spaces at all
    [InlineData("Belias ラベ10ー58", "Belias LavenderBeds 10.58")] // long-vowel mark as a dash
    [InlineData("Belias ラベ10ーー58", "-")]
    [InlineData("ＫｕｊａｔａＥｍｐｙｒｅｕｍ２１－４１", "Kujata Empyreum 21.41")] // full-width, run together
    [InlineData("パンデモ ゴブ27-10 16時close", "Pandaemonium Goblet 27.10")]
    [InlineData("パンデモ ゴブ19-5時", "-")]
    public void JapaneseWardsAndPlots(string text, string expected) => Assert.Equal(expected, P(text, "Anima"));

    [Theory]
    [InlineData("Ward 2", "-")] // a ward alone needs a district
    [InlineData("Lavender Beds - x10 y15 - Ward 2", "Lich? LavenderBeds 2._")]
    [InlineData("W5 P3", "Lich? null 5.3")]
    public void WardOnlyNeedsADistrict(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Dia♥Gobby♥w1♥p4", "Diabolos Goblet 1.4")] // nearest is the district
    [InlineData("Gobby Empy W17 - P 37", "Goblin Empyreum 17.37")] // then the world
    [InlineData("CRYSTAL / GOB / SHIRO / W25 P31", "Goblin Shirogane 25.31")]
    [InlineData("Crystal, Dia, w20 Plot 38", "Diabolos null 20.38")]
    [InlineData("Materia-LB-W22-p36", "Balmung? LavenderBeds 22.36")] // a data centre where the world goes
    [InlineData("Twin Materia-LB-W22-p36", "Balmung? LavenderBeds 22.36")]
    [InlineData("DR/ Pri/Exl/Lav Bed/W25/P6", "Excalibur LavenderBeds 25.6")]
    [InlineData("HypMist W18P35", "Hyperion Mist 18.35")] // split where the case changes
    [InlineData("TheLavenderBeds W1 P2", "Balmung? LavenderBeds 1.2")]
    [InlineData("ALPHA The Goblet W13 P11", "Alpha Goblet 13.11")]
    [InlineData("Jenova Lav.Beds W8 P58", "Jenova LavenderBeds 8.58")]
    [InlineData("Primal/Excal/LavB/W25/P6", "Excalibur LavenderBeds 25.6")]
    public void DistrictIsNearestThenWorld(string text, string expected) => Assert.Equal(expected, P(text, "Balmung"));

    [Theory]
    [InlineData("Mist a b c d e f W1 P2", "Lich? Mist 1.2")] // six words between
    [InlineData("Mist a b c d e f g W1 P2", "Lich? null 1.2")] // seven is too far
    [InlineData("Twin x y z Mist W1 P2", "Twintania Mist 1.2")] // three words before the district
    [InlineData("Twin w x y z Mist W1 P2", "Lich? Mist 1.2")]
    public void DistrictAndWorldMustBeNearby(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Ult Mist W1 P2", "Ultros", "Ultros Mist 1.2")] // prefixes resolve in the listing's region
    [InlineData("Ult Mist W1 P2", "Anima", "Ultima Mist 1.2")]
    [InlineData("Ult Mist W1 P2", null, "null? Mist 1.2")] // ambiguous anywhere
    [InlineData("Ult Mist W1 P2", "Lich", "Lich? Mist 1.2")] // none in the region, several elsewhere
    [InlineData("Sagi Mist W1 P2", "Ultros", "Sagittarius Mist 1.2")] // none in the region, one elsewhere
    [InlineData("Ul Mist W1 P2", "Ultros", "Ultros? Mist 1.2")] // too short
    [InlineData("10m gil Mist W1 P2", "Ultros", "Ultros? Mist 1.2")] // a common word
    [InlineData("Ultima Mist W1 P2", "Ultros", "Ultima Mist 1.2")] // full names resolve anywhere
    [InlineData("Mist W1 P2", "Nowhere", "Nowhere? Mist 1.2")] // an unknown listing world passes through
    public void WorldPrefixes(string text, string? listedOn, string expected) => Assert.Equal(expected, P(text, listedOn));

    [Fact]
    public void TextWithNoAddressGivesNone()
    {
        Assert.Equal("-", P(""));
        Assert.Equal("-", P("Med House in Empyreum. LF new members"));
        Assert.Equal("-", P("DJ's Saber-Shiro-Rans 9P-1A CST w/ prizes"));
    }
}
