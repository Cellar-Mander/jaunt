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
    [InlineData("Plot 30, 1st Ward Mist", "Lich? Mist 1.30")]
    [InlineData("Plot 30, 2nd Ward Mist", "Lich? Mist 2.30")]
    [InlineData("Plot 30, 3rd Ward Mist", "Lich? Mist 3.30")]
    [InlineData("5th Ward Plot 3 Mist", "Lich? Mist 5.3")] // an ordinal as the ad's first token
    [InlineData("Mist 第5, 12", "Lich? Mist 5.12")] // 第 marks the ward, so any gap before the plot will do
    [InlineData("ミスト5-12番地 qq rr Sagi", "Sagittarius Mist 5.12")] // 番地 is read, so the world is two words on
    public void WardForms(string text, string expected)
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        Assert.Equal(expected, P(text));
    }

    [Theory]
    [InlineData("Mist W5 P12 Topmast Room 3", "Lich? Mist 5.12 apt3")] // a room in an apartment building is an apartment
    [InlineData("Mist W5 P12 Room A", "Lich? Mist 5.12")] // a room needs a number
    [InlineData("Mist W5 P12 Room 0", "Lich? Mist 5.12 room0")]
    [InlineData("Mist W5 P12 0号室", "Lich? Mist 5.12 room0")]
    [InlineData("W5 P12 Room 3 Mist", "Lich? Mist 5.12 room3")]
    [InlineData("W5 P12 3号室 Mist", "Lich? Mist 5.12 room3")]
    [InlineData("Mist W5 P12 3号室 Sub", "Lich? Mist 5.12 sub room3")]
    [InlineData("Mist W5 Apt 1-24 qq rr Sagi", "Sagittarius Mist 5._ apt24")]
    [InlineData("W5 Apt 28 Mist", "Lich? Mist 5._ apt28")]
    [InlineData("W5 Apt 28号室 qq rr Mist", "Lich? Mist 5._ apt28")]
    [InlineData("Mist W5 P12 Apt 28号室", "Lich? Mist 5.12 apt28")]
    [InlineData("Mist W5 Apt qq", "Lich? Mist 5._")] // an apartment needs a number
    [InlineData("Mist W5 P12 Sub Sub Sub Sub Sub Sub Sub Sub Main", "Lich? Mist 5.12 sub")] // eight tokens of tail, no more
    [InlineData("Mist W5 P29/30 qq rr Sagi", "Sagittarius Mist 5.29 ; Sagittarius Mist 5.30")]
    [InlineData("Mist W2/30 qq rr Sagi", "Sagittarius Mist 2.30")]
    public void WhatFollowsThePlot(string text, string expected)
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        Assert.Equal(expected, P(text));
    }

    [Theory]
    [InlineData("Mist W5 P12 (Sub)(B1)", "Lich? Mist 5.12 sub roomB1")]
    [InlineData("Mist W5 P12 (Main)", "Lich? Mist 5.12 main")]
    [InlineData("Mist W5 P12 (AB12)", "Lich? Mist 5.12 roomAB12")] // four characters, the longest room code
    [InlineData("Mist W5 P12 (Sub1)", "Lich? Mist 5.12 roomSub1")] // a room code, not a subdivision
    [InlineData("Mist W5 P12 (B1 2)", "Lich? Mist 5.12")] // a room code's parts touch
    [InlineData("Mist W5 P12 (1é)", "Lich? Mist 5.12")] // and are ASCII letters and digits
    [InlineData("Mist W5 (B1)", "Lich? Mist 5._")] // a room needs a plot
    [InlineData("Mist W5 P12 (abc) (Sub", "Lich? Mist 5.12")] // an unclosed bracket is not read
    [InlineData("Mist W5 P12 (Sub qq qq qq qq qq qq qq qq qq)", "Lich? Mist 5.12 sub")] // ten tokens, the most a bracket holds
    public void Brackets(string text, string expected)
    {
        // revert-check: not revert-sensitive (kills surviving and uncovered mutants: pins existing behaviour)
        Assert.Equal(expected, P(text));
    }

    [Theory]
    [InlineData("Sagi W5 P12 Mist", "Sagittarius Mist 5.12")] // world before, district after
    [InlineData("W5 P12 Mist qq rr Sagi", "Sagittarius Mist 5.12")]
    [InlineData("W5 P12 Sagi qq rr Mist", "Sagittarius Mist 5.12")]
    [InlineData("W5 P12 Sagi Mist", "Sagittarius Mist 5.12")]
    [InlineData("Sagi qq qq qq qq Mist 5-12", "Lich? Mist 5.12")] // past a district, the world is at most three words back
    [InlineData("Sagi Mist W5 P12 ; Goblet W3 P4 qq qq qq qq Zalera", "Sagittarius Mist 5.12 ; Sagittarius Goblet 3.4")] // the last world given beats a full name elsewhere
    [InlineData("Lavender, Beds W5 P12", "Lich? null 5.12")] // a name's words are not split by punctuation
    [InlineData("Lavender.Beds W5 P12", "Lich? LavenderBeds 5.12")] // except a dot
    [InlineData("Mistɐ W5 P12", "Lich? Mist 5.12")] // U+0250 is past the Latin letters, so it doesn't join the word
    public void DistrictAndWorldAround(string text, string expected)
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        Assert.Equal(expected, P(text));
    }

    [Theory]
    [InlineData("at Sagi 6-11pm", "-")]
    [InlineData("at Sagi 6-51号室", "-")] // nor a room number
    [InlineData("Mist 5-12 (ST)", "Lich? Mist 5.12")] // a time word only after a space or nothing
    public void TimesAfterNumbers(string text, string expected)
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        Assert.Equal(expected, P(text));
    }

    [Fact]
    public void TheListingWorldIsNamedAsTheGameWritesIt()
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        Assert.Equal("Lich? Mist 5.12", P("Mist W5 P12", "lich"));
    }

    [Fact]
    public void ANameDoesNotReachIntoTheAddressBefore()
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        // A district whose first word ends the address before it: the second address reads only "Hills"
        var parser = new AddressParser(new Gazetteer([new GameWorld("Lich", "Light", "EU")], [new(District.Shirogane, "Ward Hills")]));

        var addresses = parser.Parse("Plot 30, 5th Ward Hills W3 P4", "Lich");

        Assert.Equal([null, null], addresses.Select(a => a.District));
    }

    [Theory]
    [InlineData("Mist W5 P12 qq qq qq qq Twin", "TWIN Mist 5.12")] // a full name anywhere, even one two worlds share
    [InlineData("Lic Light Mist W5 P12", "Odin? Mist 5.12")] // a data centre from the game where the world would be
    [InlineData("Lic Dyn Mist W5 P12", "Odin? Mist 5.12")] // and one players use
    [InlineData("Mists W5 P12", "Odin? Mist 5.12")] // a district name players use
    [InlineData("Exl Mist W5 P12", "Excalibur Mist 5.12")] // and a world name
    public void TheGazetteerKnowsFullNamesDataCentresAndAliases(string text, string expected)
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        // A new gazetteer, since the shared one is built before a mutant is switched on
        var parser = new AddressParser(new Gazetteer(
            [new GameWorld("Twin", "Primal", "NA"), new GameWorld("TWIN", "Light", "EU"), new GameWorld("Odin", "Light", "EU"), new GameWorld("Lich", "Light", "EU"),
                new GameWorld("Excalibur", "Primal", "NA")],
            [new(District.Mist, "Mist")]));

        Assert.Equal(expected, Short(Assert.Single(parser.Parse(text, "Odin"))));
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
    [InlineData("Plot 30, 5th Ward, Goblet, Midgard", "Midgardsormr Goblet 5.30")]
    [InlineData("Plot 8 / 26th Ward Shirogane", "Lich? Shirogane 26.8")]
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
    [InlineData("ー")] // at the start
    [InlineData("Mist 5ー")] // at the end
    [InlineData("Mist 5 ー12")] // no digit before
    [InlineData("Mist 5ーP12")] // no digit after
    public void ALongVowelMarkIsADashOnlyBetweenDigits(string text)
    {
        // revert-check: not revert-sensitive (kills surviving mutants: pins existing behaviour)
        Assert.Equal("-", P(text));
    }

    [Fact]
    public void JapaneseNamesAreKeptWholeInTheGazetteer()
    {
        // revert-check: not revert-sensitive (kills a surviving mutant: pins existing behaviour)
        // A new gazetteer, since the shared one is built before a mutant is switched on
        var parser = new AddressParser(new Gazetteer([new GameWorld("Lich", "Light", "EU")], [new(District.Shirogane, "シロガネ")]));

        Assert.Equal(District.Shirogane, Assert.Single(parser.Parse("シロガネ5-12", "Lich")).District);
    }

    [Theory]
    [InlineData("メテオベリエンピ2区52号室", "Belias Empyreum 2._ apt52")] // a room number after a ward is an apartment
    [InlineData("Ifrit エンピ6-51 4号室", "Ifrit Empyreum 6.51 room4")] // after a plot it is a room
    [InlineData("Gaia/Ifrit/6-51(若草) & 4号室", "Ifrit null 6.51 room4")]
    [InlineData("Alex/Mist30拡張アパ6号室", "Alexander Mist 30._ sub apt6")]
    [InlineData("Gungnir鯖ミスト30区拡張トップマスト53号室", "Gungnir Mist 30._ sub apt53")]
    [InlineData("マナIxion:ミスト14区表・トップマスト81号", "Ixion Mist 14._ main apt81")]
    [InlineData("ラムウミスト22-アパ1- 24", "Ramuh Mist 22._ apt24")] // building 1, room 24
    [InlineData("Ramuh ミスト22", "-")] // a bare ward needs an apartment after it
    [InlineData("Ramuh ミスト22区", "Ramuh Mist 22._")]
    [InlineData("Animaエンピ15‐2 個室001", "Anima Empyreum 15.2 room001")]
    [InlineData("槍鯖 シロガネ30-18【B1】", "Gungnir Shirogane 30.18 roomB1")]
    [InlineData("Anima ラベ18-48[地下]", "Anima LavenderBeds 18.48")] // no digit, so not a room
    [InlineData("Anima ラベ18-48【ABCDE1】", "Anima LavenderBeds 18.48")] // too long
    [InlineData("Anima ラベ18-48【B 1】", "Anima LavenderBeds 18.48")]
    [InlineData("Ifrit エンピ1-55(拡張街南東)", "Ifrit Empyreum 1.55 sub")]
    [InlineData("Ifrit エンピ1-55(拡張街南東", "Ifrit Empyreum 1.55")] // never closed
    public void JapaneseRoomsApartmentsAndSubdivisions(string text, string expected) => Assert.Equal(expected, P(text, "Anima"));

    [Theory]
    [InlineData("Shirogane w1p21 room 3", "Lich? Shirogane 1.21 room3")]
    [InlineData("Rav LB W9 LHS Room#6", "Ravana LavenderBeds 9._ apt6")] // after a building name, an apartment
    [InlineData("LB W 22 Apt 28", "Lich? LavenderBeds 22._ apt28")]
    [InlineData("Lich Mist W3 P4 subdivision", "Lich Mist 3.4 sub")]
    public void RoomsAndApartments(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Gob S W4 A15", "Lich? Goblet 4._ sub apt15")]
    [InlineData("Gob W4 A15", "Lich? Goblet 4._ apt15")]
    [InlineData("Gob S W4 P5", "Lich? Goblet 4.5 sub")]
    [InlineData("Gob S/W4 P5", "Lich? Goblet 4.5")] // only a space may come between S and the ward
    [InlineData("Mist's W4 P5", "Lich? Mist 4.5")] // a possessive s is not the subdivision
    [InlineData("Gob W4 P5 A15", "Lich? Goblet 4.5")] // a house has no apartment
    [InlineData("Gob W4 A 15", "Lich? Goblet 4._")] // the A must touch its number
    [InlineData("Gob W4 A15-3", "Lich? Goblet 4._ apt3")] // building then room, as after Apt
    public void SingleLetterSubdivisionAndApartment(string text, string expected) => Assert.Equal(expected, P(text));

    [Theory]
    [InlineData("Ward 2", "-")] // a ward alone needs a district
    [InlineData("Lavender Beds - x10 y15 - Ward 2 - Omega", "Omega LavenderBeds 2._")]
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
    [InlineData("Crystal Goblet Zalera W7 P5", "Zalera Goblet 7.5")] // the district before the world
    [InlineData("Mist Lich W7 P5", "Lich Mist 7.5")]
    [InlineData("Mist Twin Lich W7 P5", "Lich Mist 7.5")] // the nearest world
    public void DistrictIsNearestThenWorld(string text, string expected) => Assert.Equal(expected, P(text, "Balmung"));

    [Theory]
    [InlineData("Mist a b c d e f W1 P2", "Lich? Mist 1.2")] // six words between
    [InlineData("Mist a b c d e f g W1 P2", "Lich? null 1.2")] // seven is too far
    [InlineData("Twin x y z Mist W1 P2", "Twintania Mist 1.2")] // three words before the district
    [InlineData("Twin w x y z Mist W1 P2", "Lich? Mist 1.2")]
    [InlineData("Mist x y z Twin W1 P2", "Twintania Mist 1.2")] // three words before the world
    [InlineData("Mist w x y z Twin W1 P2", "Twintania null 1.2")]
    [InlineData("Grand Library open W8 P45 Mist Sagi", "Sagittarius Mist 8.45")] // after, when none before
    [InlineData("Mist W8 P45 open stage Twin", "Twintania Mist 8.45")] // but only just after
    [InlineData("Mist W8 P45 open stage now Twin", "Lich? Mist 8.45")]
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

    [Theory]
    [InlineData("Gaia/Tiamat/Goblet 20-30 のレポリット水族館、Mist 3-34", "Tiamat Goblet 20.30 ; Tiamat Mist 3.34")] // second inherits the first's world
    [InlineData("Goblet 20-30 x、Mist 3-34", "Lich? Goblet 20.30 ; Lich? Mist 3.34")] // not a listing world
    [InlineData("[Mateus・FC] FLORETTE Open Recruitment Roleplay・Content・Community Apply: LB・W6・P33", "Mateus LavenderBeds 6.33")] // a full name anywhere
    [InlineData("Raff club open now, come along to LB W6 P33", "Lich? LavenderBeds 6.33")] // but not a prefix
    public void WorldsFromElsewhereInTheAd(string text, string expected) => Assert.Equal(expected, P(text));

    [Fact]
    public void TextWithNoAddressGivesNone()
    {
        Assert.Equal("-", P(""));
        Assert.Equal("-", P("Med House in Empyreum. LF new members"));
        Assert.Equal("-", P("DJ's Saber-Shiro-Rans 9P-1A CST w/ prizes"));
    }
}
