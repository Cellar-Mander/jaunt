namespace Jaunt.Core.Text;

/// <summary>
/// Names players use that the game's data doesn't have: Japanese world names (the game writes worlds
/// only in Latin letters), abbreviations that aren't a prefix of the name, and district and data-centre
/// shorthand. Prefix abbreviations ("Krak", "Sagi") need no entry; see <see cref="Gazetteer.WorldByPrefix"/>.
/// </summary>
internal static class CommunityAliases
{
    public static readonly (string Name, string World)[] Worlds =
    [
        // Elemental
        ("イージス", "Aegis"), ("アトモス", "Atomos"), ("カーバンクル", "Carbuncle"), ("ガルーダ", "Garuda"),
        ("グングニル", "Gungnir"), ("槍", "Gungnir"), ("クジャタ", "Kujata"), ("トンベリ", "Tonberry"),
        ("テュポーン", "Typhon"),
        // Gaia
        ("アレキサンダー", "Alexander"), ("アレキ", "Alexander"), ("バハムート", "Bahamut"), ("バハ", "Bahamut"),
        ("デュランダル", "Durandal"), ("フェンリル", "Fenrir"), ("イフリート", "Ifrit"), ("リディル", "Ridill"),
        ("ティアマット", "Tiamat"), ("アルテマ", "Ultima"),
        // Mana
        ("アニマ", "Anima"), ("アスラ", "Asura"), ("チョコボ", "Chocobo"), ("ハーデス", "Hades"),
        ("イクシオン", "Ixion"), ("マサムネ", "Masamune"), ("パンデモニウム", "Pandaemonium"),
        ("パンデモ", "Pandaemonium"), ("タイタン", "Titan"),
        // Meteor
        ("ベリアス", "Belias"), ("ベリ", "Belias"), ("マンドラゴラ", "Mandragora"), ("マンドラ", "Mandragora"),
        ("ラムウ", "Ramuh"), ("神竜", "Shinryu"), ("ユニコーン", "Unicorn"), ("ユニ", "Unicorn"), ("ゆに", "Unicorn"),
        ("ヴァルファーレ", "Valefor"), ("ヴァル", "Valefor"), ("用心棒", "Yojimbo"), ("ゼロムス", "Zeromus"),
        // Latin abbreviations that aren't prefixes
        ("Exl", "Excalibur"), ("Gobby", "Goblin"),
    ];

    /// <summary>The Japanese names in <see cref="Worlds"/> that are full names rather than abbreviations.</summary>
    public static readonly HashSet<string> FullJapaneseNames =
    [
        "イージス", "アトモス", "カーバンクル", "ガルーダ", "グングニル", "クジャタ", "トンベリ", "テュポーン",
        "アレキサンダー", "バハムート", "デュランダル", "フェンリル", "イフリート", "リディル", "ティアマット", "アルテマ",
        "アニマ", "アスラ", "チョコボ", "ハーデス", "イクシオン", "マサムネ", "パンデモニウム", "タイタン",
        "ベリアス", "マンドラゴラ", "ラムウ", "神竜", "ユニコーン", "ヴァルファーレ", "用心棒", "ゼロムス",
    ];

    public static readonly string[] DataCenters = ["Dyn", "Dyna", "Cry", "Crys", "Pri", "Ele", "DC", "マナ", "メテオ", "エレ", "ガイア"];

    public static readonly KeyValuePair<District, string>[] Districts =
    [
        new(District.Mist, "Mists"), new(District.Mist, "ミスト"),
        new(District.LavenderBeds, "Lavender Beds"), new(District.LavenderBeds, "Lavender Bed"),
        new(District.LavenderBeds, "Lav Beds"), new(District.LavenderBeds, "Lav Bed"), new(District.LavenderBeds, "Lav B"),
        new(District.LavenderBeds, "Lav"), new(District.LavenderBeds, "LB"), new(District.LavenderBeds, "ラベ"),
        new(District.Goblet, "Goblet"), new(District.Goblet, "Gob"), new(District.Goblet, "Gobby"),
        new(District.Goblet, "ゴブレット"), new(District.Goblet, "ゴブ"),
        new(District.Shirogane, "Shiro"),
        new(District.Empyreum, "Empy"), new(District.Empyreum, "Emp"), new(District.Empyreum, "Em"),
        new(District.Empyreum, "Emperyum"), new(District.Empyreum, "エンピ"),
    ];

    public static readonly (string Name, Gazetteer.Keyword Keyword)[] Keywords =
    [
        ("区", Gazetteer.Keyword.WardSuffix), ("第", Gazetteer.Keyword.WardPrefix),
        ("番地", Gazetteer.Keyword.PlotSuffix), ("番", Gazetteer.Keyword.PlotSuffix),
        ("号室", Gazetteer.Keyword.RoomSuffix), ("号", Gazetteer.Keyword.RoomSuffix),
        ("アパ", Gazetteer.Keyword.Apartment), ("アパルトメント", Gazetteer.Keyword.Apartment),
        ("Apt", Gazetteer.Keyword.Apartment), ("Apartment", Gazetteer.Keyword.Apartment),
        ("個室", Gazetteer.Keyword.Room), ("Room", Gazetteer.Keyword.Room), ("Rm", Gazetteer.Keyword.Room),
        ("拡張", Gazetteer.Keyword.Subdivision), ("Subdivision", Gazetteer.Keyword.Subdivision),
        ("Sub", Gazetteer.Keyword.Subdivision),
        ("表", Gazetteer.Keyword.MainDivision), ("Main", Gazetteer.Keyword.MainDivision),
        // Apartment buildings, and the side of the ward one is on
        ("トップマスト", Gazetteer.Keyword.Building), ("リリーヒルズ", Gazetteer.Keyword.Building),
        ("スルタンズブレス", Gazetteer.Keyword.Building), ("紅梅御殿", Gazetteer.Keyword.Building),
        ("イングルサイド", Gazetteer.Keyword.Building), ("Topmast", Gazetteer.Keyword.Building),
        ("Lily Hills", Gazetteer.Keyword.Building), ("Sultana's Breath", Gazetteer.Keyword.Building),
        ("Kobai Goten", Gazetteer.Keyword.Building), ("Ingleside", Gazetteer.Keyword.Building),
        ("LHS", Gazetteer.Keyword.Building), ("RHS", Gazetteer.Keyword.Building),
    ];
}
