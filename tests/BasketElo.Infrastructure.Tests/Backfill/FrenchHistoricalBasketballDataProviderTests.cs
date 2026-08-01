using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class FrenchHistoricalBasketballDataProviderTests
{
    [Fact]
    public void ParsesMalformedBasketArchivesRoundMarkupAndRepairsRoundOrdinal()
    {
        const string html = """
            <TABLE><TR><TD><B>JOURNEE 21</B>
            <TR><TD COLSPAN=4><I>Mardi 10 avril 2001<I>
            <TR><TD>ASVEL<TD>72<TD>67<TD>Chalon</TABLE>
            <TABLE><TR><TD><B>JOURNEE 26</B>
            <TR><TD COLSPAN=4><I>Samedi 14 avril 2001</I>
            <TR><TD>Le Havre<TD>125<TD>126<TD>Le Mans (a3p)</TABLE>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseBasketArchivesGames(
            html, "2000-2001", "http://example.test/results", 2001);

        Assert.Equal(2, games.Count);
        Assert.Equal("Round 1", games[0].CompetitionRound);
        Assert.Equal("Lyon-Villeurbanne", games[0].HomeTeamName);
        Assert.Equal("Round 2", games[1].CompetitionRound);
        Assert.Equal((short)126, games[1].AwayScore);
        Assert.Equal("Le Mans", games[1].AwayTeamName);
        Assert.Equal(games.Count, games.Select(game => game.SourceGameId).Distinct().Count());
    }

    [Fact]
    public void ParsesTheSportsOvertimeScoreAndSkipsSeriesAggregate()
    {
        const string html = """
            <table class="table-style-2">
              <tr><td></td><td><a href="basketball-a-results-identity-equ1.html" title="Le Havre">Le Havre</a></td><td>2 - 0</td><td><a href="basketball-b-results-identity-equ2.html" title="Le Mans Sarthe Basket">Le Mans</a></td></tr>
              <tr><td colspan="5"><h6 class="daterenc">6 October 2007</h6></td></tr>
              <tr><td></td><td><a href="basketball-a-results-identity-equ1.html" title="STB Le Havre">Le Havre</a></td><td>104 - 110 ot</td><td><a href="basketball-b-results-identity-equ2.html" title="BCM Gravelines Dunkerque">Gravelines</a></td></tr>
            </table>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseTheSportsRound(
            html, "2007-2008", "30080", "Round 2", "Regular Season", "https://example.test/ajax");

        var game = Assert.Single(games);
        Assert.Equal(new DateTime(2007, 10, 6, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Le Havre", game.HomeTeamName);
        Assert.Equal("Gravelines-Dunkerque", game.AwayTeamName);
        Assert.Equal((short)110, game.AwayScore);
    }

    [Fact]
    public void ParsesLEquipeDatedGamesAndSkipsSeriesAggregate()
    {
        const string html = """
            <div><div class="caption caption--small">jeudi 17 sept.</div><div class="grid grid--noborder">
              <div class="TeamScore__top">
                <a class="TeamScore__team TeamScore__team--home" href="/Basket/BasketFicheClub1017.html"><span>Lyon CRO</span></a>
                <a href="/Basket/match-en-direct/pro-a-1992-1993/lyon-cro-montpellier-live/126360"><div class="TeamScore__score TeamScore__score--ended"><span>88</span><span>-</span><span>81</span></div></a>
                <a class="TeamScore__team TeamScore__team--away" href="/Basket/BasketFicheClub11.html"><span>Montpellier</span></a>
              </div>
              <div class="TeamScore__top">
                <a class="TeamScore__team TeamScore__team--home" href="/Basket/BasketFicheClub12.html"><span>Limoges CSP</span></a>
                <div class="TeamScore__score TeamScore__score--ended"><span>2</span><span>-</span><span>0</span></div>
                <a class="TeamScore__team TeamScore__team--away" href="/Basket/BasketFicheClub9.html"><span>Pau</span></a>
              </div>
            </div></div>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseLEquipeStage(
            html, "1992-1993", 1992, 1993, "1re journée", "https://example.test/1re-journee");

        var game = Assert.Single(games);
        Assert.Equal("lequipe:126360", game.SourceGameId);
        Assert.Equal(new DateTime(1992, 9, 17, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Lyon CRO", game.HomeTeamName);
        Assert.Equal("Montpellier", game.AwayTeamName);
        Assert.Equal("Regular Season", game.CompetitionPhase);
        Assert.Equal("Round 1", game.CompetitionRound);
        Assert.Equal(FrenchHistoricalBasketballDataProvider.LEquipeParserVersion, game.Provenance!.ParserVersion);
    }

    [Fact]
    public void ParsesLEquipePlayoffChildEventsWithFullDates()
    {
        const string html = """
            <div class="CalendarResults__childEvent">
              <span class="CalendarResults__childEventDate">09 avril 1993 - 15h30</span>
              <div class="TeamScore__top">
                <a class="TeamScore__team TeamScore__team--home" href="/Basket/BasketFicheClub9.html"><span>Pau-Lacq-Orthez</span></a>
                <a href="/Basket/match-en-direct/pro-a-1992-1993/pau-limoges-live/90438"><div class="TeamScore__score TeamScore__score--ended"><span>73</span><span>-</span><span>69</span></div></a>
                <a class="TeamScore__team TeamScore__team--away" href="/Basket/BasketFicheClub12.html"><span>Limoges CSP</span></a>
              </div>
            </div>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseLEquipeStage(
            html, "1992-1993", 1992, 1993, "Finale", "https://example.test/finale");

        var game = Assert.Single(games);
        Assert.Equal(new DateTime(1993, 4, 9, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Pau-Orthez", game.HomeTeamName);
        Assert.Equal("Playoffs", game.CompetitionPhase);
        Assert.Equal("Final", game.CompetitionRound);
    }

    [Fact]
    public void ParsesFrenchCupTableAndFinalPhaseTemplateWithoutDuplicates()
    {
        const string html = """
            <h3>Trente-deuxièmes de finale</h3>
            <table><tr><td>15 février 2005</td><td><a href="/wiki/Feurs">Feurs</a> (N1)</td><td>81 - 89</td><td><a href="/wiki/Olympique_d%27Antibes">Antibes</a> (Pro A)</td></tr></table>
            """;
        const string wikitext = """
            |F1-info=[[15 mai]] à [[Paris-Bercy]]
            |F1-E1=[[Cholet Basket]]
            |F1-S1=79
            |F1-E2='''[[Basket Club Maritime Gravelines Dunkerque Grand Littoral|BCM Gravelines-Dunkerque]]'''
            |F1-S2='''91'''
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseFrenchCupGames(
            html, wikitext, "2004-2005", 2004, 2005, "https://example.test/wiki", "123");

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.CompetitionRound == "Round of 32" && game.HomeTeamName == "Feurs");
        Assert.Contains(games, game => game.CompetitionRound == "Final" && game.AwayTeamName == "Gravelines-Dunkerque");
        Assert.All(games, game => Assert.Equal(FrenchHistoricalBasketballDataProvider.FrenchCupParserVersion, game.Provenance!.ParserVersion));
    }
}
