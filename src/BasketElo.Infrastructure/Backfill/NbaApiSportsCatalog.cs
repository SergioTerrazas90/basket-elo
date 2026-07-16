namespace BasketElo.Infrastructure.Backfill;

public static class NbaApiSportsCatalog
{
    public const string LeagueId = "12";

    private static readonly IReadOnlyDictionary<string, string> Franchises =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["132"] = "Atlanta Hawks",
            ["133"] = "Boston Celtics",
            ["134"] = "Brooklyn Nets",
            ["135"] = "Charlotte Hornets",
            ["136"] = "Chicago Bulls",
            ["137"] = "Cleveland Cavaliers",
            ["138"] = "Dallas Mavericks",
            ["139"] = "Denver Nuggets",
            ["140"] = "Detroit Pistons",
            ["141"] = "Golden State Warriors",
            ["142"] = "Houston Rockets",
            ["143"] = "Indiana Pacers",
            ["144"] = "Los Angeles Clippers",
            ["145"] = "Los Angeles Lakers",
            ["146"] = "Memphis Grizzlies",
            ["147"] = "Miami Heat",
            ["148"] = "Milwaukee Bucks",
            ["149"] = "Minnesota Timberwolves",
            ["150"] = "New Orleans Pelicans",
            ["151"] = "New York Knicks",
            ["152"] = "Oklahoma City Thunder",
            ["153"] = "Orlando Magic",
            ["154"] = "Philadelphia 76ers",
            ["155"] = "Phoenix Suns",
            ["156"] = "Portland Trail Blazers",
            ["157"] = "Sacramento Kings",
            ["158"] = "San Antonio Spurs",
            ["159"] = "Toronto Raptors",
            ["160"] = "Utah Jazz",
            ["161"] = "Washington Wizards"
        };

    private static readonly IReadOnlyDictionary<string, DateOnly> RegularSeasonOpeners =
        new Dictionary<string, DateOnly>(StringComparer.Ordinal)
        {
            ["2008-2009"] = new(2008, 10, 28),
            ["2009-2010"] = new(2009, 10, 27),
            ["2010-2011"] = new(2010, 10, 26),
            ["2011-2012"] = new(2011, 12, 25),
            ["2012-2013"] = new(2012, 10, 30),
            ["2013-2014"] = new(2013, 10, 29),
            ["2014-2015"] = new(2014, 10, 28),
            ["2015-2016"] = new(2015, 10, 27),
            ["2016-2017"] = new(2016, 10, 25),
            ["2017-2018"] = new(2017, 10, 17),
            ["2018-2019"] = new(2018, 10, 16),
            ["2019-2020"] = new(2019, 10, 22),
            ["2020-2021"] = new(2020, 12, 22),
            ["2021-2022"] = new(2021, 10, 19),
            ["2022-2023"] = new(2022, 10, 18),
            ["2023-2024"] = new(2023, 10, 24),
            ["2024-2025"] = new(2024, 10, 22),
            ["2025-2026"] = new(2025, 10, 21)
        };

    public static string? GetCanonicalName(string sourceTeamId) =>
        Franchises.GetValueOrDefault(sourceTeamId);

    public static string? GetExclusionReason(
        string leagueId,
        string season,
        DateTime gameDateTimeUtc,
        string homeSourceTeamId,
        string awaySourceTeamId)
    {
        if (!string.Equals(leagueId, LeagueId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!Franchises.ContainsKey(homeSourceTeamId) || !Franchises.ContainsKey(awaySourceTeamId))
        {
            return "nba-non-franchise-exhibition";
        }

        if (!RegularSeasonOpeners.TryGetValue(season, out var opener))
        {
            return "nba-unreviewed-season";
        }

        return DateOnly.FromDateTime(gameDateTimeUtc) < opener
            ? "nba-preseason"
            : null;
    }
}
