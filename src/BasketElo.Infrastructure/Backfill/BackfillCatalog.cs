using BasketElo.Domain.Backfill;
using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillCatalog : IBackfillCatalog
{
    private static readonly IReadOnlyCollection<ConfiguredBackfillLeague> Leagues =
    [
        new("fivethirtyeight", "United States", "NBA", "United States: NBA", "1946-1947", EndSeason: "2007-2008", EloPoolKey: EloPoolKeys.Nba),
        new("api-sports", "USA", "NBA", "United States: NBA", "2008-2009", EndSeason: "2025-2026", EloPoolKey: EloPoolKeys.Nba),
        new("api-sports", "Spain", "ACB", "Spain: ACB", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Spain", "Spanish Cup", "Spain: Copa del Rey", "2008", CompetitionType: "domestic_cup"),
        new("api-sports", "Spain", "Supercopa ACB", "Spain: Supercopa ACB", "2010", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "Europe", "Euroleague", "International: Euroleague", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Europe", "Eurocup", "International: EuroCup", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Europe", "ABA League", "International: ABA League", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Europe", "ABA Supercup", "International: ABA Supercup", "2017", CompetitionType: "international_cup", EndSeason: "2023"),
        new("api-sports", "Europe", "Alpe Adria Cup", "International: Alpe Adria Cup", "2015", CompetitionType: "international", EndSeason: "2023"),
        new("api-sports", "Europe", "Baltic League", "International: Baltic League", "2009", CompetitionType: "international", EndSeason: "2017"),
        new(
            "api-sports",
            "Europe",
            "BIBL",
            "International: Balkan International Basketball League",
            "2008",
            ProviderLeagues:
            [
                new("Europe", "BIBL", "bibl")
            ],
            CompetitionType: "international",
            EndSeason: "2023"),
        new(
            "api-sports",
            "Europe",
            "BNXT League",
            "International: BNXT League",
            "2021-2022",
            ProviderLeagues:
            [
                new("Europe", "BNXT League", "literal")
            ],
            CompetitionType: "international",
            EndSeason: "2025-2026"),
        new("api-sports", "Europe", "Champions League", "International: Basketball Champions League", "2016-2017", EndSeason: "2025-2026"),
        new(
            "api-sports",
            "Europe",
            "ENBL",
            "International: European North Basketball League",
            "2021-2022",
            ProviderLeagues:
            [
                new("Europe", "ENBL", "literal")
            ],
            CompetitionType: "international",
            EndSeason: "2025-2026"),
        new("api-sports", "Europe", "FIBA Europe Cup", "International: FIBA Europe Cup", "2015-2016", EndSeason: "2025-2026"),
        new(
            "api-sports",
            "Europe",
            "Liga Unike",
            "International: Liga Unike",
            "2020-2021",
            ProviderLeagues:
            [
                new("Europe", "Liga Unike", "literal")
            ],
            CompetitionType: "international",
            EndSeason: "2025-2026"),
        new("api-sports", "France", "LNB", "France: LNB Pro A / Betclic Elite", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "France", "French Cup", "France: Coupe de France", "2008", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "France", "LNB Super Cup", "France: LNB Super Cup", "2011", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "France", "Semaine Des As", "France: Leaders Cup / Semaine des As", "2011", CompetitionType: "domestic_cup"),
        new("api-sports", "Lithuania", "LKL", "Lithuania: LKL", "2011-2012", EndSeason: "2025-2026"),
        new("api-sports", "Lithuania", "King Mindaugas Cup", "Lithuania: King Mindaugas Cup", "2020-2021", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Lithuania", "LKF Cup", "Lithuania: LKF Cup", "2018", CompetitionType: "domestic_cup", EndSeason: "2019"),
        new("api-sports", "Greece", "A1", "Greece: A1 / Greek Basket League", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Greece", "Greek Cup", "Greece: Greek Cup", "2008-2009", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Greece", "Super Cup", "Greece: Super Cup", "2020", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "Italy", "Lega A", "Italy: Lega Basket Serie A", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Italy", "Italian Cup", "Italy: Italian Cup", "2009", CompetitionType: "domestic_cup"),
        new("api-sports", "Italy", "Lega A - Super Cup", "Italy: Lega A Super Cup", "2011-2012", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Turkey", "Super Ligi", "Turkey: BSL", "2016-2017", EndSeason: "2025-2026"),
        new("api-sports", "Turkey", "Turkish Cup", "Turkey: Turkish Cup", "2010-2011", EndSeason: "2025-2026"),
        new("api-sports", "Turkey", "Super Cup", "Turkey: Super Cup", "2011", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new(
            "api-sports",
            "Europe",
            "Latvia-Estonian League",
            "Latvia/Estonia: Latvia-Estonian League",
            "2018-2019",
            ProviderLeagues:
            [
                new("Europe", "Latvia-Estonian League"),
                new("Latvia", "LBL")
            ],
            EndSeason: "2025-2026"),
        new("api-sports", "Latvia", "Latvian Cup", "Latvia: Latvian Cup", "2024", CompetitionType: "domestic_cup", EndSeason: "2026"),
        new(
            "api-sports",
            "Belgium",
            "EuroMillions Basketball League",
            "Belgium: Belgian Top Tier",
            "2010-2011",
            ProviderLeagues:
            [
                new("Belgium", "EuroMillions Basketball League"),
                new("Belgium", "Pro Basketball League", "end_year")
            ],
            EndSeason: "2025-2026"),
        new("api-sports", "Belgium", "Belgian Cup", "Belgium: Belgian Cup", "2012", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "Germany", "BBL", "Germany: BBL", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Germany", "German Cup", "Germany: German Cup", "2008-2009", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Germany", "Super Cup", "Germany: Super Cup", "2011", CompetitionType: "domestic_cup", EndSeason: "2015"),
        new("api-sports", "Israel", "Super League", "Israel: BSL", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Israel", "Israel Cup", "Israel: Israel Cup", "2008-2009", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Israel", "League Cup", "Israel: League Cup", "2010", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "Poland", "Tauron Basket Liga", "Poland: PLK", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Poland", "Polish Cup", "Poland: Polish Cup", "2016", CompetitionType: "domestic_cup", EndSeason: "2026"),
        new("api-sports", "Poland", "PBA Cup", "Poland: PBA Cup", "2015", CompetitionType: "domestic_cup", EndSeason: "2017"),
        new("api-sports", "Poland", "Super Cup", "Poland: Super Cup", "2011", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new("api-sports", "Czech Republic", "NBL", "Czech Republic: NBL", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Czech Republic", "Czech Cup", "Czech Republic: Czech Cup", "2010-2011", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Russia", "VTB United League Promo-Cup", "Russia: Top Tier", "2010-2011", EndSeason: "2025-2026"),
        new("api-sports", "Russia", "Russian Cup", "Russia: Russian Cup", "2008-2009", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Russia", "VTB Super Cup", "Russia: VTB Super Cup", "2021", CompetitionType: "domestic_cup", EndSeason: "2025"),
        new(
            "api-sports",
            "Serbia",
            "First League",
            "Serbia: First League",
            "2010-2011",
            ProviderLeagues:
            [
                new("Serbia", "First League"),
                new("Serbia", "Super League", "end_year")
            ],
            EndSeason: "2025-2026"),
        new("api-sports", "Serbia", "Korac cup", "Serbia: Korac Cup", "2012", CompetitionType: "domestic_cup"),
        new("api-sports", "Croatia", "Premijer liga", "Croatia: Premijer liga", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Croatia", "Croatian Cup", "Croatia: Croatian Cup", "2012-2013", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Slovenia", "Liga UPC", "Slovenia: Liga Nova KBM", "2008-2009", EndSeason: "2025-2026"),
        new("api-sports", "Slovenia", "Slovenian Cup", "Slovenia: Slovenian Cup", "2013-2014", CompetitionType: "domestic_cup", EndSeason: "2025-2026"),
        new("api-sports", "Slovenia", "Supercup", "Slovenia: Supercup", "2012", CompetitionType: "domestic_cup", EndSeason: "2025"),

        // Global Sports Archive is the primary source for the men's international
        // tournaments below. A zero request limit means the provider discovers
        // and traverses every stage and gameweek/page for each edition.
        new("global-sports-archive", "Africa", "FIBA AfroBasket", "Africa: FIBA AfroBasket", "1962", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(1962, 1964, 1965, 1968, 1970, 1972, 1974, 1975, 1978, 1980, 1981, 1983, 1985, 1987, 1989, 1992, 1993, 1995, 1997, 1999, 2001, 2003, 2005, 2007, 2009, 2011, 2013, 2015, 2017, 2021, 2025), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Africa", "FIBA AfroBasket Qualifiers", "Africa: FIBA AfroBasket Qualifiers", "2021", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2021, 2025), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Africa", "FIBA AfroBasket Pre-Qualifiers", "Africa: FIBA AfroBasket Pre-Qualifiers", "2021", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2021, 2025), UsesSingleYearSeasonLabel: true),
        new("fiba", "Africa", "FIBA AfroBasket Pre-Qualifiers", "Africa: FIBA AfroBasket Pre-Qualifiers", "2021", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2021), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "World", "FIBA WC Qualification", "World: FIBA WC Qualification", "2019", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2019, 2023, 2027), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Spain", "ACB", "Global Sports Archive: ACB COVID fallback", "2019-2020", ExplicitSeasons: ["2019-2020"]),
        new("global-sports-archive", "Europe", "ABA League", "Global Sports Archive: ABA League COVID fallback", "2019-2020", CompetitionType: "international", ExplicitSeasons: ["2019-2020"]),
        new("global-sports-archive", "Europe", "BIBL", "Global Sports Archive: BIBL COVID fallback", "2019-2020", CompetitionType: "international", ExplicitSeasons: ["2019-2020"]),
        new("global-sports-archive", "Europe", "Champions League", "Global Sports Archive: Champions League COVID fallback", "2019-2020", CompetitionType: "international", ExplicitSeasons: ["2019-2020"]),
        new("global-sports-archive", "Europe", "Eurocup", "Global Sports Archive: EuroCup COVID fallback", "2019-2020", CompetitionType: "international", ExplicitSeasons: ["2019-2020"]),
        new("global-sports-archive", "Europe", "Euroleague", "Global Sports Archive: Euroleague COVID fallback", "2019-2020", CompetitionType: "international", ExplicitSeasons: ["2019-2020"]),
        new("global-sports-archive", "Asia", "FIBA Asia Cup", "Asia: FIBA Asia Cup", "1960", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(1960, 1963, 1965, 1967, 1969, 1971, 1973, 1975, 1977, 1979, 1981, 1983, 1985, 1987, 1989, 1991, 1993, 1995, 1997, 1999, 2001, 2003, 2005, 2007, 2009, 2011, 2013, 2015, 2017, 2022, 2025), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Asia", "FIBA Asia Cup Qualification", "Asia: FIBA Asia Cup Qualification", "2021", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2021, 2025), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Asia", "Asian Games", "Asia: Asian Games", "2018", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2018, 2022), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Europe", "EuroBasket", "Europe: EuroBasket", "1935", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(1935, 1937, 1939, 1946, 1947, 1949, 1951, 1953, 1955, 1957, 1959, 1961, 1963, 1965, 1967, 1969, 1971, 1973, 1975, 1977, 1979, 1981, 1983, 1985, 1987, 1989, 1991, 1993, 1995, 1997, 1999, 2001, 2003, 2005, 2007, 2009, 2011, 2013, 2015, 2017, 2022, 2025), UsesSingleYearSeasonLabel: true),
        new("fiba", "Europe", "FIBA EuroBasket Division B", "Europe: FIBA EuroBasket Division B", "2007", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2007, 2009, 2011), UsesSingleYearSeasonLabel: true),
        // 2021 and 2025 are represented by the reconciled GSA records below; the
        // FIBA row is intentionally limited to seasons without GSA coverage.
        new("fiba", "Europe", "FIBA EuroBasket Pre-Qualifiers", "Europe: FIBA EuroBasket Pre-Qualifiers", "1995", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2003, 2001, 1999, 1997, 1995), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Europe", "FIBA EuroBasket Pre-Qualifiers", "Europe: FIBA EuroBasket Pre-Qualifiers", "2021", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2029, 2025, 2021), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "Europe", "EuroBasket Qualifiers", "Europe: EuroBasket Qualifiers", "2017", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2017, 2021, 2025), UsesSingleYearSeasonLabel: true),
        new("fiba", "Europe", "EuroBasket Qualifiers", "Europe: EuroBasket Qualifiers", "2015", ProviderLeagues: [new("Europe", "FIBA EuroBasket Qualifiers", "year")], CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2015, 2013, 2011, 2009, 2007, 2005, 2003, 2001, 1999, 1997, 1995), UsesSingleYearSeasonLabel: true),
        new(
            "wikipedia",
            "Europe",
            "EuroBasket Qualifiers",
            "Europe: EuroBasket Qualifiers",
            "1991",
            ProviderLeagues: [new("Europe", "EuroBasket Qualifiers", "year")],
            CompetitionType: "qualifier",
            EloPoolKey: EloPoolKeys.NationalTeams,
            ExplicitSeasons: Years(1993, 1991),
            UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "World", "FIBA Basketball World Cup", "World: FIBA Basketball World Cup", "1950", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(1950, 1954, 1959, 1963, 1967, 1970, 1974, 1978, 1982, 1986, 1990, 1994, 1998, 2002, 2006, 2010, 2014, 2019, 2023), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "World", "Summer Olympics", "World: Summer Olympics (men)", "2012", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2012, 2016, 2020, 2024), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "World", "Olympics Qualification", "World: Olympics Qualification", "2016", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2016, 2020, 2024), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "World", "FIBA AmeriCup", "World: FIBA AmeriCup", "1980", CompetitionType: "international", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(1980, 1984, 1988, 1989, 1992, 1993, 1995, 1997, 1999, 2001, 2003, 2005, 2007, 2009, 2011, 2013, 2015, 2017, 2022, 2025), UsesSingleYearSeasonLabel: true),
        new("global-sports-archive", "World", "FIBA AmeriCup Qualification", "World: FIBA AmeriCup Qualification", "2022", CompetitionType: "qualifier", EloPoolKey: EloPoolKeys.NationalTeams, ExplicitSeasons: Years(2022, 2025), UsesSingleYearSeasonLabel: true)
    ];

    public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => Leagues;

    public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league)
    {
        if (league.ExplicitSeasons is not null)
        {
            return league.ExplicitSeasons;
        }

        var startYear = SeasonLabelNormalizer.ParseStartYear(league.StartSeason);
        var currentSeasonStart = !string.IsNullOrWhiteSpace(league.EndSeason)
            ? SeasonLabelNormalizer.ParseStartYear(league.EndSeason)
            : GetCurrentSeasonStartYear();
        var seasons = new List<string>();

        for (var year = startYear; year <= currentSeasonStart; year++)
        {
            seasons.Add($"{year}-{year + 1}");
        }

        return seasons;
    }

    private static int GetCurrentSeasonStartYear()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 7 ? now.Year : now.Year - 1;
    }

    private static string[] Years(params int[] years)
        => years.Select(year => year.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
