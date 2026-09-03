using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillCatalog : IBackfillCatalog
{
    private static readonly IReadOnlyCollection<ConfiguredBackfillLeague> Leagues =
    [
        new("api-sports", "Spain", "ACB", "Spain: ACB", "2000-2001"),
        new("api-sports", "Spain", "Spanish Cup", "Spain: Copa del Rey", "2007-2008", CupSeasons(2008, 2026), CupSeasonMap(2008, 2026)),
        new("api-sports", "Spain", "Supercopa ACB", "Spain: Supercopa ACB", "2009-2010", CupSeasons(2010, 2025, 2019), CupSeasonMap(2010, 2025, 2019)),
        new("api-sports", "France", "LNB", "France: LNB Pro A / Betclic Elite", "2000-2001"),
        new("api-sports", "France", "French Cup", "France: French Cup", "2007-2008", CupSeasons(2008, 2025, 2009), CupSeasonMap(2008, 2025, 2009)),
        new("api-sports", "France", "LNB Super Cup", "France: LNB Super Cup", "2010-2011", MergeSeasons(
            CupSeasons(2011, 2017),
            CupSeasons(2025, 2025)), MergeSeasonMaps(
            CupSeasonMap(2011, 2017),
            CupSeasonMap(2025, 2025))),
        new("api-sports", "France", "Semaine Des As", "France: Semaine Des As / Leaders Cup", "2010-2011", MergeSeasons(
            CupSeasons(2011, 2020),
            CupSeasons(2023, 2026)), MergeSeasonMaps(
            CupSeasonMap(2011, 2020),
            CupSeasonMap(2023, 2026))),
        new("api-sports", "Lithuania", "LKL", "Lithuania: LKL", "2000-2001"),
        new("api-sports", "Lithuania", "King Mindaugas Cup", "Lithuania: King Mindaugas Cup", "2020-2021", SplitYears(2020, 2025)),
        new("api-sports", "Lithuania", "LKF Cup", "Lithuania: LKF Cup", "2017-2018", CupSeasons(2018, 2019), CupSeasonMap(2018, 2019)),
        new("api-sports", "Greece", "Basket League", "Greece: A1 / Greek Basket League", "2000-2001"),
        new("api-sports", "Greece", "Greek Cup", "Greece: Greek Cup", "2008-2009", SplitYears(2008, 2025)),
        new("api-sports", "Greece", "Super Cup", "Greece: Super Cup", "2019-2020", CupSeasons(2020, 2025), CupSeasonMap(2020, 2025)),
        new("api-sports", "Italy", "Lega A", "Italy: Lega Basket Serie A", "2000-2001"),
        new("api-sports", "Italy", "Italian Cup", "Italy: Coppa Italia", "2008-2009", CupSeasons(2009, 2026), CupSeasonMap(2009, 2026)),
        new("api-sports", "Italy", "Lega A - Super Cup", "Italy: Supercoppa", "2011", MergeSeasons(
            CupSeasons(2011, 2018),
            CupSeasons(2020, 2020),
            SplitYears(2021, 2025)), MergeSeasonMaps(
            CupSeasonMap(2011, 2018),
            CupSeasonMap(2020, 2020))),
        new("api-sports", "Turkey", "Super Ligi", "Turkey: BSL", "2000-2001"),
        new("api-sports", "Turkey", "Turkish Cup", "Turkey: Turkish Cup", "2010-2011", SplitYears(2010, 2025)),
        new("api-sports", "Turkey", "Super Cup", "Turkey: Super Cup", "2010-2011", CupSeasons(2011, 2025), CupSeasonMap(2011, 2025)),
        new("api-sports", "Latvia", "LBL", "Latvia: LBL", "2000-2001"),
        new("api-sports", "Latvia", "Latvian Cup", "Latvia: Latvian Cup", "2023-2024", CupSeasons(2024, 2026), CupSeasonMap(2024, 2026)),
        new("api-sports", "Belgium", "EuroMillions Basketball League", "Belgium: Belgian Top Tier", "2000-2001"),
        new("api-sports", "Belgium", "Pro Basketball League", "Belgium: Pro Basketball League", "2021-2022", CupSeasons(2022, 2025), CupSeasonMap(2022, 2025)),
        new("api-sports", "Belgium", "Belgian Cup", "Belgium: Belgian Cup", "2011-2012", CupSeasons(2012, 2025), CupSeasonMap(2012, 2025)),
        new("api-sports", "Germany", "BBL", "Germany: BBL", "2000-2001"),
        new("api-sports", "Germany", "German Cup", "Germany: German Cup", "2008-2009", SplitYears(2008, 2025)),
        new("api-sports", "Germany", "Super Cup", "Germany: Super Cup", "2010-2011", CupSeasons(2011, 2015), CupSeasonMap(2011, 2015)),
        new("api-sports", "Israel", "Super League", "Israel: BSL", "2000-2001"),
        new("api-sports", "Israel", "Israel Cup", "Israel: Israel Cup", "2008-2009", SplitYears(2008, 2025)),
        new("api-sports", "Israel", "League Cup", "Israel: League Cup", "2009-2010", CupSeasons(2010, 2025), CupSeasonMap(2010, 2025)),
        new("api-sports", "Poland", "Tauron Basket Liga", "Poland: PLK", "2000-2001"),
        new("api-sports", "Poland", "Polish Cup", "Poland: Polish Cup", "2015-2016", CupSeasons(2016, 2026), CupSeasonMap(2016, 2026)),
        new("api-sports", "Poland", "Super Cup", "Poland: Super Cup", "2010-2011", CupSeasons(2011, 2025), CupSeasonMap(2011, 2025)),
        new("api-sports", "Czech Republic", "NBL", "Czech Republic: NBL", "2000-2001"),
        new("api-sports", "Czech Republic", "Czech Cup", "Czech Republic: Czech Cup", "2010-2011", SplitYears(2010, 2025)),
        new("api-sports", "Russia", "Super League", "Russia: Top Tier", "2000-2001"),
        new("api-sports", "Russia", "PBL", "Russia: PBL", "2011-2012", SplitYears(2011, 2012)),
        new("api-sports", "Russia", "Russian Cup", "Russia: Russian Cup", "2008-2009", SplitYears(2008, 2025)),
        new("api-sports", "Russia", "VTB Super Cup", "Russia: VTB Super Cup", "2020-2021", CupSeasons(2021, 2025), CupSeasonMap(2021, 2025))
    ];

    public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => Leagues;

    public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league)
    {
        if (league.SeasonsOverride is { Count: > 0 })
        {
            return league.SeasonsOverride;
        }

        var startYear = ParseStartYear(league.StartSeason);
        var currentSeasonStart = GetCurrentSeasonStartYear();
        var seasons = new List<string>();

        for (var year = startYear; year <= currentSeasonStart; year++)
        {
            seasons.Add($"{year}-{year + 1}");
        }

        return seasons;
    }

    private static int ParseStartYear(string season)
    {
        var parsed = season.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parsed.Length > 0 && int.TryParse(parsed[0], out var startYear) ? startYear : 2000;
    }

    private static int GetCurrentSeasonStartYear()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 7 ? now.Year : now.Year - 1;
    }

    private static IReadOnlyCollection<string> CalendarYears(int startYear, int endYear, params int[] excludedYears)
    {
        var excluded = excludedYears.ToHashSet();
        return Enumerable.Range(startYear, endYear - startYear + 1)
            .Where(year => !excluded.Contains(year))
            .Select(year => year.ToString())
            .ToList();
    }

    private static IReadOnlyCollection<string> SplitYears(int startYear, int endYear, params int[] excludedStartYears)
    {
        var excluded = excludedStartYears.ToHashSet();
        return Enumerable.Range(startYear, endYear - startYear + 1)
            .Where(year => !excluded.Contains(year))
            .Select(year => $"{year}-{year + 1}")
            .ToList();
    }

    private static IReadOnlyCollection<string> MergeSeasons(params IReadOnlyCollection<string>[] groups)
    {
        return groups
            .SelectMany(x => x)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyCollection<string> CupSeasons(int startYear, int endYear, params int[] excludedYears)
    {
        var excluded = excludedYears.ToHashSet();
        return Enumerable.Range(startYear, endYear - startYear + 1)
            .Where(year => !excluded.Contains(year))
            .Select(year => $"{year - 1}-{year}")
            .ToList();
    }

    private static IReadOnlyDictionary<string, string> CupSeasonMap(int startYear, int endYear, params int[] excludedYears)
    {
        var excluded = excludedYears.ToHashSet();
        return Enumerable.Range(startYear, endYear - startYear + 1)
            .Where(year => !excluded.Contains(year))
            .ToDictionary(year => $"{year - 1}-{year}", year => year.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> MergeSeasonMaps(params IReadOnlyDictionary<string, string>[] groups)
    {
        return groups
            .SelectMany(x => x)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
    }
}
