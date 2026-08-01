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

    [Fact]
    public void ParsesOfficialGallicaSeniorSlateAndStopsBeforeEspoirs()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="29"/><String CONTENT="Septembre"/><String CONTENT="1984"/></TextLine>
              <TextLine><String CONTENT="Nationale"/><String CONTENT="Masculine"/><String CONTENT="-"/><String CONTENT="1"/></TextLine>
              <TextLine><String CONTENT="1er"/><String CONTENT="TOUR"/><String CONTENT="—"/><String CONTENT="ALLER"/></TextLine>
              <TextLine><String CONTENT="SCM"/><String CONTENT="Le"/><String CONTENT="Mans"/><String CONTENT="99"/><String CONTENT="EB"/><String CONTENT="Orthez"/><String CONTENT="90"/></TextLine>
              <TextLine><String CONTENT="AS"/><String CONTENT="Monaco"/><String CONTENT="75"/><String CONTENT="CSP"/><String CONTENT="Limoges"/><String CONTENT="93"/></TextLine>
              <TextLine><String CONTENT="Tours"/><String CONTENT="BC"/><String CONTENT="102"/><String CONTENT="CA"/><String CONTENT="St-Etienne"/><String CONTENT="76"/></TextLine>
              <TextLine><String CONTENT="Mulhouse"/><String CONTENT="BC"/><String CONTENT="85"/><String CONTENT="JA"/><String CONTENT="Vichy"/><String CONTENT="72"/></TextLine>
              <TextLine><String CONTENT="ASVEL"/><String CONTENT="110"/><String CONTENT="ES"/><String CONTENT="Avignon"/><String CONTENT="88"/></TextLine>
              <TextLine><String CONTENT="ESM"/><String CONTENT="Challans"/><String CONTENT="86/01."/><String CONTENT="Antibes"/><String CONTENT="83"/></TextLine>
              <TextLine><String CONTENT="Stade"/><String CONTENT="Français"/><String CONTENT="94"/><String CONTENT="Caen"/><String CONTENT="BC"/><String CONTENT="82"/></TextLine>
              <TextLine><String CONTENT="Espoirs"/><String CONTENT="-"/><String CONTENT="N.M."/><String CONTENT="1"/></TextLine>
              <TextLine><String CONTENT="SCM"/><String CONTENT="Le"/><String CONTENT="Mans"/><String CONTENT="64"/><String CONTENT="EB"/><String CONTENT="Orthez"/><String CONTENT="84"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1984-1985", 1985, 109, "https://example.test/f109.item");

        Assert.Equal(7, games.Count);
        Assert.Contains(games, game => game.HomeTeamName == "Le Mans" && game.HomeScore == 99 && game.AwayTeamName == "Pau-Orthez");
        Assert.Contains(games, game => game.HomeTeamName == "Challans" && game.HomeScore == 86 && game.AwayTeamName == "Antibes");
        Assert.DoesNotContain(games, game => game.HomeScore == 64);
        Assert.All(games, game => Assert.Equal(FrenchHistoricalBasketballDataProvider.GallicaParserVersion, game.Provenance!.ParserVersion));
    }

    [Fact]
    public void ParsesGallicaN1AAndN1BButRejectsTheirEspoirsDuplicates()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="22"/><String CONTENT="NOVEMBRE"/><String CONTENT="1986"/></TextLine>
              <TextLine><String CONTENT="MASCULINE"/><String CONTENT="1"/><String CONTENT="A"/></TextLine>
              <TextLine><String CONTENT="1er"/><String CONTENT="TOUR"/><String CONTENT="—"/><String CONTENT="ALLER"/></TextLine>
              <TextLine><String CONTENT="AS"/><String CONTENT="Monaco"/><String CONTENT="101"/><String CONTENT="Challans"/><String CONTENT="BVC"/><String CONTENT="73"/></TextLine>
              <TextLine><String CONTENT="MASCULINE"/><String CONTENT="1"/><String CONTENT="B"/></TextLine>
              <TextLine><String CONTENT="1er"/><String CONTENT="TOUR"/><String CONTENT="—"/><String CONTENT="ALLER"/></TextLine>
              <TextLine><String CONTENT="Reims"/><String CONTENT="CB"/><String CONTENT="87"/><String CONTENT="Tours"/><String CONTENT="BC"/><String CONTENT="77"/></TextLine>
              <TextLine><String CONTENT="ESPOIRS"/></TextLine>
              <TextLine><String CONTENT="NATIONALE"/><String CONTENT="MASCULINE"/><String CONTENT="1"/><String CONTENT="A"/></TextLine>
              <TextLine><String CONTENT="AS"/><String CONTENT="Monaco"/><String CONTENT="70"/><String CONTENT="Challans"/><String CONTENT="BVC"/><String CONTENT="72"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1986-1987", 1987, 102, "https://example.test/f102.item");

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.CompetitionRound!.Contains("N1A") && game.HomeScore == 101);
        Assert.Contains(games, game => game.CompetitionRound!.Contains("N1B") && game.HomeTeamName == "Reims");
    }

    [Fact]
    public void ParsesUndatedGallicaPlayoffsAndUsesStarredTeamAsHome()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="NM1"/></TextLine>
              <TextLine><String CONTENT="LES"/><String CONTENT="RÉSULTATS"/><String CONTENT="DES"/><String CONTENT="PLAY-OFF"/></TextLine>
              <TextLine><String CONTENT="1/8"/><String CONTENT="DE"/><String CONTENT="FINALE"/></TextLine>
              <TextLine><String CONTENT="Orthez"/><String CONTENT="bat"/><String CONTENT="*Caen"/><String CONTENT="92-91"/></TextLine>
              <TextLine><String CONTENT="Orthez*"/><String CONTENT="bat"/><String CONTENT="Caen"/><String CONTENT="83-66"/></TextLine>
              <TextLine><String CONTENT="QUALIFICATION"/><String CONTENT="KORAC"/></TextLine>
              <TextLine><String CONTENT="Racing"/><String CONTENT="b."/><String CONTENT="Lorient"/><String CONTENT="97-80"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1986-1987", 1987, 15, "https://example.test/f15.item");

        Assert.Equal(2, games.Count);
        Assert.Equal("Caen", games[0].HomeTeamName);
        Assert.Equal((short)91, games[0].HomeScore);
        Assert.Equal("Pau-Orthez", games[0].AwayTeamName);
        Assert.Equal((short)92, games[0].AwayScore);
        Assert.Equal(new DateTime(1987, 3, 21, 12, 0, 0, DateTimeKind.Utc), games[0].GameDateTimeUtc);
        Assert.Equal(new DateTime(1987, 3, 28, 12, 0, 0, DateTimeKind.Utc), games[1].GameDateTimeUtc);
    }

    [Fact]
    public void RepairsGallicaScoreGlyphsAndTeamOcrWithoutLeakingPre1986Pools()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="23"/><String CONTENT="FEVRIER"/><String CONTENT="1985"/></TextLine>
              <TextLine><String CONTENT="NATIONALE"/><String CONTENT="MASCULINE"/><String CONTENT="1"/></TextLine>
              <TextLine><String CONTENT="5e"/><String CONTENT="TOUR"/><String CONTENT="—"/><String CONTENT="RETOUR"/></TextLine>
              <TextLine><String CONTENT="01."/><String CONTENT="Antibes"/><String CONTENT="lOO/CA"/><String CONTENT="St-Etienne"/><String CONTENT="82"/></TextLine>
              <TextLine><String CONTENT="ES"/><String CONTENT="Avignor,"/><String CONTENT="110"/><String CONTENT="SCM"/><String CONTENT="Le"/><String CONTENT="Mans"/><String CONTENT="103"/></TextLine>
              <TextLine><String CONTENT="POULE"/><String CONTENT="A"/></TextLine>
              <TextLine><String CONTENT="ASVEL"/><String CONTENT="90"/><String CONTENT="CSP"/><String CONTENT="Limoges"/><String CONTENT="80"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1984-1985", 1985, 206, "https://example.test/f206.item");

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.HomeTeamName == "Antibes" && game.HomeScore == 100 && game.AwayTeamName == "Saint-Étienne");
        Assert.Contains(games, game => game.HomeTeamName == "Avignon" && game.HomeScore == 110 && game.AwayTeamName == "Le Mans");
    }

    [Fact]
    public void RepairsGallicaOrdinalDateAndLyonOcr()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="1e"/><String CONTENT="OCTOBRE"/><String CONTENT="1983"/></TextLine>
              <TextLine><String CONTENT="NATIONALE"/><String CONTENT="MASCULINE"/><String CONTENT="1"/></TextLine>
              <TextLine><String CONTENT="1er"/><String CONTENT="TOUR"/><String CONTENT="ALLER"/></TextLine>
              <TextLine><String CONTENT="LN,on"/><String CONTENT="CRO"/><String CONTENT="91"/><String CONTENT="JA"/><String CONTENT="Vichy"/><String CONTENT="83"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var game = Assert.Single(FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1983-1984", 1984, 99, "https://example.test/f99.item"));

        Assert.Equal(new DateTime(1983, 10, 1, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Lyon", game.HomeTeamName);
        Assert.Equal("Vichy", game.AwayTeamName);
    }

    [Fact]
    public void RejectsPlayoffStyleSectionsBeforeTheLeagueIntroducedPlayoffs()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="NM1"/></TextLine>
              <TextLine><String CONTENT="LES"/><String CONTENT="RESULTATS"/><String CONTENT="DES"/><String CONTENT="PLAY-OFF"/></TextLine>
              <TextLine><String CONTENT="1/8"/><String CONTENT="DE"/><String CONTENT="FINALE"/></TextLine>
              <TextLine><String CONTENT="Orthez"/><String CONTENT="bat"/><String CONTENT="*Caen"/><String CONTENT="92-91"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1982-1983", 1983, 15, "https://example.test/f15.item");

        Assert.Empty(games);
    }

    [Fact]
    public void KeepsOnlyTheTopFlightGroupsInThe1985To1986TransitionSeason()
    {
        const string alto = """
            <alto xmlns="http://bibnum.bnf.fr/ns/alto_prod"><Layout><Page><PrintSpace><TextBlock>
              <TextLine><String CONTENT="14"/><String CONTENT="SEPTEMBRE"/><String CONTENT="1985"/></TextLine>
              <TextLine><String CONTENT="MASCULINE"/><String CONTENT="1"/><String CONTENT="A"/></TextLine>
              <TextLine><String CONTENT="1er"/><String CONTENT="TOUR"/><String CONTENT="ALLER"/></TextLine>
              <TextLine><String CONTENT="AS"/><String CONTENT="Monaco"/><String CONTENT="101"/><String CONTENT="Challans"/><String CONTENT="BVC"/><String CONTENT="73"/></TextLine>
              <TextLine><String CONTENT="MASCULINE"/><String CONTENT="1"/><String CONTENT="B"/></TextLine>
              <TextLine><String CONTENT="1er"/><String CONTENT="TOUR"/><String CONTENT="ALLER"/></TextLine>
              <TextLine><String CONTENT="Reims"/><String CONTENT="CB"/><String CONTENT="87"/><String CONTENT="Tours"/><String CONTENT="BC"/><String CONTENT="77"/></TextLine>
              <TextLine><String CONTENT="NATIONALE"/><String CONTENT="MASCULINE"/><String CONTENT="1"/></TextLine>
              <TextLine><String CONTENT="POULE"/><String CONTENT="1"/></TextLine>
              <TextLine><String CONTENT="2e"/><String CONTENT="TOUR"/><String CONTENT="RETOUR"/></TextLine>
              <TextLine><String CONTENT="CSP"/><String CONTENT="Limoges"/><String CONTENT="92"/><String CONTENT="EB"/><String CONTENT="Orthez"/><String CONTENT="88"/></TextLine>
              <TextLine><String CONTENT="POULE"/><String CONTENT="2"/></TextLine>
              <TextLine><String CONTENT="ASVEL"/><String CONTENT="90"/><String CONTENT="Vichy"/><String CONTENT="80"/></TextLine>
            </TextBlock></PrintSpace></Page></Layout></alto>
            """;

        var games = FrenchHistoricalBasketballDataProvider.ParseGallicaAltoPage(
            alto, "1985-1986", 1986, 102, "https://example.test/f102.item");

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.HomeTeamName == "Monaco");
        Assert.Contains(games, game => game.HomeTeamName == "Limoges");
        Assert.DoesNotContain(games, game => game.HomeTeamName == "Reims" || game.HomeTeamName == "Lyon-Villeurbanne");
    }
}
