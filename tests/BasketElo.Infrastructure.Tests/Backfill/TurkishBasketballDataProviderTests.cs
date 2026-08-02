using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class TurkishBasketballDataProviderTests
{
    [Fact]
    public void ParsesTBLStatTeamIndexAndDatedTeamSchedule()
    {
        const string teamsHtml = """
            <a onclick="go('team/4/0708')"><img src="x"><br>Fenerbahçe Ülker</a>
            <a onclick="go('team/1/0708')"><img src="x"><br>Efes Pilsen</a>
            """;
        const string teamHtml = """
            <table><tr><td class="c">NS 01</td><td class="c">13.10.2007</td><td>iç. Efes Pilsen | <a onclick="go('game/42001')">G 80 - 59</a></td></tr>
            <tr><td class="c">PÇ 01</td><td class="c">04.05.2008</td><td>dış. Efes Pilsen | <a onclick="go('game/42244')">G 78 - 73</a></td></tr>
            """;

        var teams = TurkishBasketballDataProvider.ParseTeamsPage(teamsHtml, "0708");
        var current = Assert.Single(teams, x => x.CanonicalName == "Fenerbahçe");
        var sourceIds = teams.ToDictionary(x => x.CanonicalName, x => x.SourceTeamId, StringComparer.OrdinalIgnoreCase);
        var games = TurkishBasketballDataProvider.ParseTeamSchedule(
            teamHtml,
            current,
            sourceIds,
            "2007-2008",
            "https://bsl.tblstat.net/team/4/0708",
            DateTime.UtcNow);

        Assert.Equal(2, games.Count);
        var regular = games.Single(x => x.SourceGameId == "tblstat-game-42001");
        Assert.Equal(new DateTime(2007, 10, 13), regular.GameDateTimeUtc.Date);
        Assert.Equal("Regular season", regular.CompetitionPhase);
        Assert.Equal("Fenerbahçe", regular.HomeTeamName);
        Assert.Equal("Anadolu Efes", regular.AwayTeamName);
        Assert.Equal((short)80, regular.HomeScore);
        Assert.Equal((short)59, regular.AwayScore);

        var playoff = games.Single(x => x.SourceGameId == "tblstat-game-42244");
        Assert.Equal("Playoffs", playoff.CompetitionPhase);
        Assert.Equal("Anadolu Efes", playoff.HomeTeamName);
        Assert.Equal("Fenerbahçe", playoff.AwayTeamName);
        Assert.Equal((short)78, playoff.HomeScore);
        Assert.Equal((short)73, playoff.AwayScore);
    }

    [Fact]
    public void UsesDeterministicIdsWhenOlderTBLStatRowsHaveNoGameLinks()
    {
        const string teamHtml = """
            <table><tr><td class="c">NS 1D</td><td class="c">14.01.1967</td><td>iç. Karşıyaka | <span>G 76 - 46</span></td></tr></table>
            """;
        var current = new TurkishBasketballDataProvider.TeamRef("55", "Altınordu", "Altınordu", "turkey:club:altınordu");
        var sourceIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Altınordu"] = current.SourceTeamId,
            ["Karşıyaka"] = "turkey:club:karşıyaka"
        };

        var first = TurkishBasketballDataProvider.ParseTeamSchedule(
            teamHtml, current, sourceIds, "1966-1967", "https://bsl.tblstat.net/team/55/6667", DateTime.UtcNow);
        var second = TurkishBasketballDataProvider.ParseTeamSchedule(
            teamHtml, current, sourceIds, "1966-1967", "https://bsl.tblstat.net/team/55/6667", DateTime.UtcNow);

        var game = Assert.Single(first);
        Assert.StartsWith("tblstat-synthetic-", game.SourceGameId, StringComparison.Ordinal);
        Assert.Equal(game.SourceGameId, Assert.Single(second).SourceGameId);
        Assert.Equal("NS 1D", game.CompetitionRound);
    }

    [Fact]
    public void KeepsVenueOrderForAwayTeamScheduleRows()
    {
        const string teamHtml = """
            <table><tr><td class="c">NS 1D</td><td class="c">14.01.1967</td><td>dış. Karşıyaka | <span>M 53 - 48</span></td></tr></table>
            """;
        var current = new TurkishBasketballDataProvider.TeamRef("49", "Suspor", "Suspor", "turkey:club:suspor");
        var sourceIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Suspor"] = current.SourceTeamId,
            ["Karşıyaka"] = "turkey:club:karşıyaka"
        };

        var game = Assert.Single(TurkishBasketballDataProvider.ParseTeamSchedule(
            teamHtml, current, sourceIds, "1966-1967", "https://bsl.tblstat.net/team/49/6667", DateTime.UtcNow));

        Assert.Equal("Karşıyaka", game.HomeTeamName);
        Assert.Equal("Suspor", game.AwayTeamName);
        Assert.Equal((short)53, game.HomeScore);
        Assert.Equal((short)48, game.AwayScore);
    }

    [Fact]
    public void CatalogAddsHistoricalTurkeyLeagueCupAndSuperCupSegments()
    {
        var catalog = new BackfillCatalog();
        var league = Assert.Single(catalog.GetLeagues(), x =>
            x.Provider == TurkishBasketballDataProvider.Source && x.LeagueName == "Super Ligi");
        var cup = Assert.Single(catalog.GetLeagues(), x =>
            x.Provider == TurkishBasketballDataProvider.Source && x.LeagueName == "Turkish Cup");
        var superCup = Assert.Single(catalog.GetLeagues(), x =>
            x.Provider == TurkishBasketballDataProvider.Source && x.LeagueName == "Super Cup");

        var leagueSeasons = catalog.GetSeasonsForLeague(league).ToList();
        var cupSeasons = catalog.GetSeasonsForLeague(cup).ToList();
        var superCupSeasons = catalog.GetSeasonsForLeague(superCup).ToList();

        Assert.Equal(50, leagueSeasons.Count);
        Assert.Equal("1966-1967", leagueSeasons[0]);
        Assert.Equal("2015-2016", leagueSeasons[^1]);
        Assert.Equal(27, cupSeasons.Count);
        Assert.Contains("1966-1967", cupSeasons);
        Assert.Contains("1991-1992", cupSeasons);
        Assert.Contains("2007-2008", cupSeasons);
        Assert.Contains("2010-2011", cupSeasons);
        Assert.DoesNotContain("1973-1974", cupSeasons);
        Assert.Equal(26, superCupSeasons.Count);
        Assert.Equal("1985", superCupSeasons[0]);
        Assert.Equal("2010", superCupSeasons[^1]);
        Assert.Equal("domestic_cup", cup.CompetitionType);
        Assert.Equal("domestic_cup", superCup.CompetitionType);
    }

    [Fact]
    public void HistoricalCupAndSuperCupImportPublishedFinalRecords()
    {
        var cup = TurkishBasketballDataProvider.ParseFinals("Turkish Cup", "2007-2008");
        var superCup = TurkishBasketballDataProvider.ParseFinals("Super Cup", "2007");

        var cupFinal = Assert.Single(cup);
        Assert.Equal("Türk Telekom", cupFinal.HomeTeamName);
        Assert.Equal((short)80, cupFinal.HomeScore);
        Assert.Equal((short)61, cupFinal.AwayScore);
        Assert.Equal("Final", cupFinal.CompetitionRound);

        var superCupFinal = Assert.Single(superCup);
        Assert.Equal("Fenerbahçe", superCupFinal.HomeTeamName);
        Assert.Equal((short)79, superCupFinal.HomeScore);
        Assert.Equal((short)77, superCupFinal.AwayScore);

        var recentCup = Assert.Single(TurkishBasketballDataProvider.ParseFinals("Turkish Cup", "2010-2011"));
        Assert.Equal("Fenerbahçe", recentCup.HomeTeamName);
        Assert.Equal((short)81, recentCup.HomeScore);

        var recentSuperCup = Assert.Single(TurkishBasketballDataProvider.ParseFinals("Super Cup", "2010"));
        Assert.Equal("Anadolu Efes", recentSuperCup.HomeTeamName);
        Assert.Equal((short)79, recentSuperCup.HomeScore);
    }
}
