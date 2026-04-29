using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public class BackfillCatalog : IBackfillCatalog
{
    private static readonly IReadOnlyCollection<ConfiguredBackfillLeague> Leagues =
    [
        new("api-sports", "Spain", "ACB", "Spain: ACB", "2000-2001"),
        new("api-sports", "France", "LNB Pro A", "France: LNB Pro A / Betclic Elite", "2000-2001"),
        new("api-sports", "Lithuania", "LKL", "Lithuania: LKL", "2000-2001"),
        new("api-sports", "Greece", "A1 Ethniki", "Greece: A1 / Greek Basket League", "2000-2001"),
        new("api-sports", "Italy", "Serie A", "Italy: Lega Basket Serie A", "2000-2001"),
        new("api-sports", "Turkey", "BSL", "Turkey: BSL", "2000-2001"),
        new("api-sports", "Latvia", "LBL", "Latvia: LBL", "2000-2001"),
        new("api-sports", "Belgium", "BLB", "Belgium: Belgian Top Tier", "2000-2001"),
        new("api-sports", "Germany", "BBL", "Germany: BBL", "2000-2001"),
        new("api-sports", "Israel", "BSL", "Israel: BSL", "2000-2001"),
        new("api-sports", "Poland", "PLK", "Poland: PLK", "2000-2001"),
        new("api-sports", "Czech Republic", "NBL", "Czech Republic: NBL", "2000-2001"),
        new("api-sports", "Russia", "Russia Top Tier", "Russia: Top Tier", "2000-2001")
    ];

    public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => Leagues;

    public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league)
    {
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
}
