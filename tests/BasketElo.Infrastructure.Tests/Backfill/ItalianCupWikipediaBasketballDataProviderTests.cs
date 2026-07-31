using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class ItalianCupWikipediaBasketballDataProviderTests
{
    [Fact]
    public void ParsesFinalEightBracketWithRoundDatesAndStableLinkedTeamIds()
    {
        const string wikitext = """
            |data_inizio = 7 febbraio [[2008]]
            {{Torneo-quarti
            | RD1=Quarti di finale<br />7 febbraio [[2008]]
            | RD2=Semifinali<br />9 febbraio [[2008]]
            | RD3=Finale<br />10 febbraio [[2008]]
            | RD1-team1=[[Olimpia Milano|Armani Jeans Milano]]
            | RD1-team2=[[Pallacanestro Cantù|Tisettanta Cantù]]
            | RD1-score1=77
            | RD1-score2=78
            | RD1-team3=[[Virtus Pallacanestro Bologna|La Fortezza Bologna]]
            | RD1-team4=[[Pallacanestro Virtus Roma|Lottomatica Roma]]
            | RD1-score3=75
            | RD1-score4=69
            | RD2-team1=[[Pallacanestro Cantù|Tisettanta Cantù]]
            | RD2-team2=[[Virtus Pallacanestro Bologna|La Fortezza Bologna]]
            | RD2-score1=80
            | RD2-score2=88
            | RD3-team1=[[Virtus Pallacanestro Bologna|La Fortezza Bologna]]
            | RD3-team2=[[Felice Scandone Basket Avellino|AIR Avellino]]
            | RD3-score1=67
            | RD3-score2=73
            }}
            """;

        var warnings = new List<string>();
        var games = ItalianCupWikipediaBasketballDataProvider.ParseGames(
            wikitext,
            "2007-2008",
            "https://example.test/2008",
            new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            "123",
            warnings).ToList();

        Assert.Equal(4, games.Count);
        var quarterFinal = Assert.Single(games, game => game.HomeTeamName == "Armani Jeans Milano");
        Assert.Equal("wiki-team:olimpia-milano", quarterFinal.SourceHomeTeamId);
        Assert.Equal(new DateTime(2008, 2, 7, 0, 0, 0, DateTimeKind.Utc), quarterFinal.GameDateTimeUtc);
        Assert.Equal("Quarti di finale", quarterFinal.CompetitionRound);
        Assert.Contains(games, game => game.CompetitionRound == "Finale" && game.AwayTeamName == "AIR Avellino");
    }

    [Fact]
    public void ParsesNamedFirstAndSecondLegBracketScoresWithoutImportingAggregates()
    {
        const string wikitext = """
            |data_inizio = 19 ottobre 1969
            {{Torneo
            | RD1 = Ottavi di finale<br />19 e 23 ottobre [[1969]]
            | RD1-team01 = [[Brill Cagliari]]
            | RD1-team02 = '''[[All'Onestà Milano]]'''
            | RD1-score01firstleg =63
            | RD1-score02firstleg =71
            | RD1-score01secondleg =54
            | RD1-score02secondleg =52
            | RD1-score01aggregate =117
            | RD1-score02aggregate =123
            }}
            """;

        var games = Parse(wikitext, "1969-1970");

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game =>
            game.HomeTeamName == "All'Onestà Milano" && game.AwayTeamName == "Brill Cagliari" &&
            game.HomeScore == 52 && game.AwayScore == 54);
        Assert.DoesNotContain(games, game => game.HomeScore == 117 || game.AwayScore == 123);
    }

    [Fact]
    public void ParsesTwoLegTablesAndReversesTheReturnLegHomeTeam()
    {
        const string wikitext = """
            |data_inizio = 1 settembre 1994
            ==Ottavi di finale==
            6 e 8 settembre [[1994]]
            {| class="wikitable"
            |'''Incontro'''
            |'''Andata'''
            |'''Ritorno'''
            |-
            |align="center"|[[Olimpia Pistoia]] - '''[[Pallacanestro Treviso|Benetton Treviso]]'''
            |align="center"|81-81
            |align="center"|64-74
            |-
            |align="center"|[[Reyer Venezia]] - '''[[Victoria Libertas Pesaro|Scavolini Pesaro]]'''
            |align="center"|68-82
            |align="center"|57-88
            |}
            """;

        var games = Parse(wikitext, "1994-1995");

        Assert.Equal(4, games.Count);
        Assert.Contains(games, game =>
            game.HomeTeamName == "Benetton Treviso" && game.AwayTeamName == "Olimpia Pistoia" &&
            game.HomeScore == 74 && game.AwayScore == 64 &&
            game.GameDateTimeUtc == new DateTime(1994, 9, 8, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ParsesEveryDirectedGameInRoundRobinResultMatrix()
    {
        const string wikitext = """
            |data_inizio = 12 settembre 1989
            ==Girone A==
            12, 19 e 26 settembre [[1989]]
            {| class="wikitable"
            !Risultati girone A!!MI!!TO!!SS
            |-
            |'''[[Olimpia Milano|Philips Milano]]'''||&nbsp;||90-88||103-78
            |-
            |'''[[Auxilium Torino|Ipifim Torino]]'''||130-108||&nbsp;||73-71
            |-
            |'''[[Dinamo Basket Sassari|Banca Popolare Sassari]]'''||84-101||96-104||&nbsp;
            |}
            """;

        var games = Parse(wikitext, "1989-1990");

        Assert.Equal(6, games.Count);
        Assert.Contains(games, game =>
            game.HomeTeamName == "Philips Milano" && game.AwayTeamName == "Ipifim Torino" &&
            game.HomeScore == 90 && game.AwayScore == 88);
        Assert.Contains(games, game =>
            game.HomeTeamName == "Ipifim Torino" && game.AwayTeamName == "Philips Milano" &&
            game.HomeScore == 130 && game.AwayScore == 108);
    }

    [Fact]
    public void ParsesTwoLegResultTemplateAndExcludesAdministrativeResultFromElo()
    {
        const string wikitext = """
            |data_inizio = 29 agosto 1996
            == Risultati ==
            === Sedicesimi di finale ===
            29 agosto e 1 settembre [[1996]]
            {{TwoLeg start}}
            {{TwoLegResult|[[Juve Caserta]]||133-159|[[Basket Rimini Crabs|Koncret Rimini]]||73-78|66-81|winner=2}}
            {{TwoLegEnd}}
            === Finale ===
            22 marzo [[1997]]
            *'''[[Kinder Bologna]]''' - [[Polti Cantù]] 2-0
            """;

        var games = Parse(wikitext, "1996-1997");

        Assert.Equal(3, games.Count);
        Assert.Contains(games, game =>
            game.HomeTeamName == "Koncret Rimini" && game.AwayTeamName == "Juve Caserta" &&
            game.HomeScore == 81 && game.AwayScore == 66);
        var forfeit = Assert.Single(games, game => game.HomeScore == 2 && game.AwayScore == 0);
        Assert.NotNull(forfeit.ExclusionReason);
    }

    [Fact]
    public void NormalizesVerifiedSponsorOnlyTeamIdsWithoutCollapsingCityRivals()
    {
        const string wikitext = """
            |data_inizio = 1 settembre 1989
            == Risultati ==
            * [[Knorr Bologna]] - [[Virtus Roma]] 88-80
            * [[Enichem Livorno]] - [[Pallacanestro Livorno|Garessio 2000 Livorno]] 91-87
            * [[Divarese Varese]] - [[Ipifim Torino]] 79-75
            * [[Koncret Rimini]] - [[Basket Arese]] 83-78
            """;

        var games = Parse(wikitext, "1989-1990");

        Assert.Equal(4, games.Count);
        var bologna = Assert.Single(games, game => game.HomeTeamName == "Knorr Bologna");
        Assert.Equal("wiki-team:virtus-pallacanestro-bologna", bologna.SourceHomeTeamId);
        Assert.Equal("wiki-team:pallacanestro-virtus-roma", bologna.SourceAwayTeamId);
        var livorno = Assert.Single(games, game => game.HomeTeamName == "Enichem Livorno");
        Assert.NotEqual(livorno.SourceHomeTeamId, livorno.SourceAwayTeamId);
        Assert.Equal("wiki-team:libertas-livorno", livorno.SourceHomeTeamId);
        Assert.Equal("wiki-team:pallacanestro-livorno", livorno.SourceAwayTeamId);
        var varese = Assert.Single(games, game => game.HomeTeamName == "Divarese Varese");
        Assert.Equal("wiki-team:pallacanestro-varese", varese.SourceHomeTeamId);
        Assert.Equal("wiki-team:auxilium-torino", varese.SourceAwayTeamId);
        var rimini = Assert.Single(games, game => game.HomeTeamName == "Koncret Rimini");
        Assert.Equal("wiki-team:basket-rimini-crabs", rimini.SourceHomeTeamId);
        Assert.Equal("wiki-team:basket-arese", rimini.SourceAwayTeamId);
    }

    [Fact]
    public void MapsSnaideroToTheCorrectUdineClubForTheSeason()
    {
        const string wikitext = "* [[Snaidero Udine]] - [[Virtus Bologna]] 80-75";

        var historical = Assert.Single(Parse(wikitext, "1971-1972"));
        var relaunched = Assert.Single(Parse(wikitext, "2005-2006"));

        Assert.Equal("wiki-team:associazione-pallacanestro-udinese", historical.SourceHomeTeamId);
        Assert.Equal("wiki-team:pallalcesto-amatori-udine", relaunched.SourceHomeTeamId);
    }

    private static List<BasketElo.Domain.Backfill.BasketballProviderGame> Parse(string wikitext, string season)
    {
        var warnings = new List<string>();
        return ItalianCupWikipediaBasketballDataProvider.ParseGames(
            wikitext,
            season,
            "https://example.test/cup",
            new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            "123",
            warnings).ToList();
    }
}
