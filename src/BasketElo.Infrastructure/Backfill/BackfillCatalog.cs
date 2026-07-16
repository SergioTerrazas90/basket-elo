using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillCatalog : IBackfillCatalog
{
    private static readonly IReadOnlyCollection<ConfiguredBackfillLeague> Leagues =
    [
        new("basketball-reference", "United States", "NBA", "United States: NBA", "1946-1947"),
        new("api-sports", "USA", "NBA", "USA: NBA", "2008-2009", EndSeason: "2025-2026"),
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
        new("api-sports", "Slovenia", "Supercup", "Slovenia: Supercup", "2012", CompetitionType: "domestic_cup", EndSeason: "2025")
    ];

    public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => Leagues;

    public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league)
    {
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
}
