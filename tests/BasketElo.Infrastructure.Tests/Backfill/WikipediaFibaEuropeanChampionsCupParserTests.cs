using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class WikipediaFibaEuropeanChampionsCupParserTests
{
    [Theory]
    [InlineData(1967, "1967Ã¢â‚¬â€œ68 FIBA European Cup Winners' Cup")]
    [InlineData(1991, "1991Ã¢â‚¬â€œ92 FIBA European Cup")]
    [InlineData(1996, "1996Ã¢â‚¬â€œ97 FIBA EuroCup")]
    [InlineData(1999, "1999Ã¢â‚¬â€œ2000 FIBA Saporta Cup")]
    [InlineData(2001, "2001Ã¢â‚¬â€œ02 FIBA Saporta Cup")]
    public void BuildsSaportaEditionPageTitles(int startYear, string expected)
    {
        Assert.StartsWith(startYear.ToString(), expected);
        var expectedTitle = startYear switch
        {
            1967 => "1967\u201368 FIBA European Cup Winners' Cup",
            1991 => "1991\u201392 FIBA European Cup",
            1996 => "1996\u201397 FIBA EuroCup",
            1999 => "1999\u20132000 FIBA Saporta Cup",
            2001 => "2001\u201302 FIBA Saporta Cup",
            _ => throw new ArgumentOutOfRangeException(nameof(startYear))
        };
        Assert.Equal(expectedTitle, WikipediaFibaEuropeanChampionsCupParser.SaportaEnglishPageTitle(startYear));
    }

    [Theory]
    [InlineData(1971, "1972 FIBA KoraÃ„â€¡ Cup")]
    [InlineData(1972, "1972Ã¢â‚¬â€œ73 FIBA KoraÃ„â€¡ Cup")]
    [InlineData(2000, "2000Ã¢â‚¬â€œ01 FIBA KoraÃ„â€¡ Cup")]
    [InlineData(2001, "2001Ã¢â‚¬â€œ02 FIBA KoraÃ„â€¡ Cup")]
    public void BuildsKoracEditionPageTitles(int startYear, string expected)
    {
        Assert.Equal(expected, WikipediaFibaEuropeanChampionsCupParser.KoracEnglishPageTitle(startYear));
    }

    [Fact]
    public void UsesTheWikipediaSeasonTitleForTheSecondKoracEdition()
    {
        Assert.Equal("1973 FIBA Kora\u0107 Cup", WikipediaFibaEuropeanChampionsCupParser.KoracWikipediaPageTitle(1972));
    }

    [Fact]
    public void UsesSeasonStartWhenInfoboxDurationIsNotParseable()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | duration = September 1999 - April 2000
            == Final ==
            {{TwoLegResult|[[Team Alpha]]|ITA|83-76|[[Team Beta]]|ESP|0-0}}
            """,
            "1999-2000",
            "https://en.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.NotEmpty(games);
        Assert.All(games, game => Assert.NotEqual(DateTime.MinValue, game.GameDateTimeUtc));
    }

    [Fact]
    public void DoesNotTreatScoreMatrixCellsAsTeams()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            {| class="wikitable"
            |-
            | [[Team Alpha]]
            |
            | 77-88
            | 76-77
            | 73-78
            |}
            """,
            "2001-2002",
            "https://en.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.DoesNotContain(games, game =>
            game.HomeTeamName is "77-88" or "76-77" or "73-78" ||
            game.AwayTeamName is "77-88" or "76-77" or "73-78");
    }

    [Fact]
    public void ParsesTwoLegResultWithReversedReturnLegAndDatedFinal()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | duration = 4 de noviembre de 1971 <br /> 23 de marzo de 1972
            == Primera ronda ==
            {{TwoLegStart}}
            {{TwoLegResult|[[Team Alpha]]|ITA|140-130|[[Team Beta]]|ESP|80-70|60-60|ganador=1}}
            |}
            == Final ==
            {{Partido de baloncesto|date=23 de marzo de 1972|team1=[[Team Alpha]]|score1=70|team2=[[Team Beta]]|score2=69}}
            """,
            "1971-1972",
            "https://es.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(3, games.Count);
        Assert.Contains(games, game => game.SourceGameId.StartsWith("wiki-fiba-", StringComparison.Ordinal));
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-beta" && game.SourceAwayTeamId == "wiki-team:team-alpha" && game.HomeScore == 60 && game.AwayScore == 60);
        Assert.Contains(games, game => game.GameDateTimeUtc == new DateTime(1972, 3, 23, 0, 0, 0, DateTimeKind.Utc) && game.HomeScore == 70 && game.AwayScore == 69);
    }

    [Fact]
    public void ParsesLegacyFiveColumnWikipediaTables()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | duration = 4 de noviembre de 1958 <br /> 28 de junio de 1959
            == Ronda preliminar ==
            {|
            |-
            | style="text-align:right" | [[Team Alpha]]
            | style="text-align:center" | 140-67
            | style="text-align:left" | [[Team Beta]]
            | style="text-align:center" | 77-40
            | style="text-align:center" | 63-27
            |}
            """,
            "1958-1959",
            "https://es.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.HomeScore == 77 && game.AwayScore == 40 && game.SourceHomeTeamId == "wiki-team:team-alpha");
        Assert.Contains(games, game => game.HomeScore == 27 && game.AwayScore == 63 && game.SourceHomeTeamId == "wiki-team:team-beta");
    }

    [Fact]
    public void ParsesSingleLegTiebreakAndThreeLegResults()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | duration = 17 de noviembre de 1962 <br /> 1 de agosto de 1963
            == Primera ronda ==
            {{TwoLegResult|[[Team Alpha]]|MAR|60-110|[[Team Beta]]|ITA|60-110||ganador=2}}
            == Cuartos de final ==
            *Un partido de desempate se celebrÃƒÂ³ el 2 de abril de 1963: [[Team Gamma]] - [[Team Delta]] 77Ã¢â‚¬â€œ65.
            == Final ==
            {{ThreeLegResult|[[Team Alpha]]|ESP|240-259|[[Team Beta]]|URS|86-69|74-91|80-99*|ganador=2}}
            """,
            "1962-1963",
            "https://es.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(5, games.Count);
        Assert.Contains(games, game => game.HomeScore == 60 && game.AwayScore == 110);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-gamma" && game.HomeScore == 77 && game.AwayScore == 65);
        Assert.Contains(games, game => game.HomeScore == 80 && game.AwayScore == 99);
    }

    [Fact]
    public void ParsesEnglishRoundRobinScoreMatrixWithoutDuplicatingMirroredGames()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | duration = 18 September 1996 Ã¢â‚¬â€œ 24 April 1997
            == Preliminary round ==
            === Group A ===
            {|
            ! Pos !! Team !! Pld !! W !! L !! PF !! PA !! PD !! Pts !! Qualification !!  !! AAA !! BBB !! CCC
            |-
            | 1 || [[Team Alpha]] || 4 || 3 || 1 || 300 || 280 || +20 || 7 || Advance ||  || Ã¢â‚¬â€ || 80Ã¢â‚¬â€œ70 || 75Ã¢â‚¬â€œ68
            |-
            | 2 || [[Team Beta]] || 4 || 2 || 2 || 280 || 290 || -10 || 6 ||  ||  || 72Ã¢â‚¬â€œ74 || Ã¢â‚¬â€ || 81Ã¢â‚¬â€œ79
            |-
            | 3 || [[Team Gamma]] || 4 || 1 || 3 || 270 || 280 || -10 || 5 ||  ||  || 65Ã¢â‚¬â€œ70 || 77Ã¢â‚¬â€œ73 || Ã¢â‚¬â€
            |}
            """,
            "1996-1997",
            "https://en.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(3, games.Count);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-alpha" && game.SourceAwayTeamId == "wiki-team:team-beta" && game.HomeScore == 80 && game.AwayScore == 70);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-alpha" && game.SourceAwayTeamId == "wiki-team:team-gamma" && game.HomeScore == 75 && game.AwayScore == 68);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-beta" && game.SourceAwayTeamId == "wiki-team:team-gamma" && game.HomeScore == 81 && game.AwayScore == 79);
    }

    [Fact]
    public void ParsesGermanCompactRoundRobinScoreMatrixWithColonScores()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | dauer = 11. November 2003 Ã¢â‚¬â€œ 13. April 2004
            == Gruppenphase ==
            === Gruppe A ===
            {|
            ! &nbsp; !! Alpha !! Beta !! Gamma
            |-
            | Alpha || * || 80:70 || 75:68
            |-
            | Beta || 72:74 || * || 81:79
            |-
            | Gamma || 65:70 || 77:73 || *
            |}
            """,
            "2003-2004",
            "https://de.wikipedia.org/wiki/ULEB_Cup_2003/04",
            DateTime.UtcNow,
            "123",
            warnings,
            sourceGameIdPrefix: "wiki-uleb",
            preserveRoundRobinMatrixHomeAway: true);

        Assert.Equal(6, games.Count);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:alpha" && game.SourceAwayTeamId == "wiki-team:beta" && game.HomeScore == 80 && game.AwayScore == 70);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:beta" && game.SourceAwayTeamId == "wiki-team:alpha" && game.HomeScore == 72 && game.AwayScore == 74);
    }

    [Fact]
    public void ParsesGermanWikipediaMatrixRowsWithCellAttributes()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            == Gruppenphase ==
            === Gruppe A ===
            {| style="border-collapse:collapse"
            !width=130|&nbsp;
            !width=80| Alpha
            !width=80| Beta
            !width=80| Gamma
            !width=80| Delta
            |- align=center
            |align=left| Alpha
            |*||80:70||75:68||77:66
            |- align=center
            |align=left| Beta
            |72:74||*||81:79||70:69
            |- align=center
            |align=left| Gamma
            |65:70||77:73||*||88:80
            |- align=center
            |align=left| Delta
            |66:77||69:70||80:88||*
            |}
            """,
            "2003-2004",
            "https://de.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings,
            sourceGameIdPrefix: "wiki-uleb",
            preserveRoundRobinMatrixHomeAway: true);

        Assert.Equal(12, games.Count);
    }

    [Fact]
    public void ParsesRenderedEnglishSportsTableMatrix()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseHtmlMatrixGames(
            """
            <html><body>
            <table>
              <tr><th>Pos</th><th>Team</th><th>Pld</th><th>Qualification</th><th></th><th>AAA</th><th>BBB</th><th>CCC</th></tr>
              <tr><td>1</td><td><a href="/wiki/Team_Alpha">Team Alpha</a></td><td>4</td><td>Advance</td><td></td><td>Ã¢â‚¬â€</td><td>80Ã¢â‚¬â€œ70</td><td>75Ã¢â‚¬â€œ68</td></tr>
              <tr><td>2</td><td><a href="/wiki/Team_Beta">Team Beta</a></td><td>4</td><td></td><td></td><td>72Ã¢â‚¬â€œ74</td><td>Ã¢â‚¬â€</td><td>81Ã¢â‚¬â€œ79</td></tr>
              <tr><td>3</td><td><a href="/wiki/Team_Gamma">Team Gamma</a></td><td>4</td><td></td><td></td><td>65Ã¢â‚¬â€œ70</td><td>77Ã¢â‚¬â€œ73</td><td>Ã¢â‚¬â€</td></tr>
            </table>
            </body></html>
            """,
            "1996-1997",
            "https://en.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(3, games.Count);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-alpha" && game.SourceAwayTeamId == "wiki-team:team-beta" && game.HomeScore == 80 && game.AwayScore == 70);
    }

    [Fact]
    public void ParsesWikipediaSportsTableTemplateMatches()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseGames(
            """
            | duration = 18 September 1996 Ã¢â‚¬â€œ 24 April 1997
            == Preliminary round ==
            === Group A ===
            {{#invoke:Sports table|main|style=WL|section=Group A|show_matches=true
            |team_order=AAA,BBB,CCC
            |match_AAA_BBB=80-70
            |match_AAA_CCC=75-68
            |match_BBB_CCC=81-79
            |name_AAA={{flagicon|ITA}} [[Team Alpha]]
            |name_BBB={{flagicon|ESP}} [[Team Beta]]
            |name_CCC={{flagicon|GRE}} [[Team Gamma]]
            }}
            """,
            "1996-1997",
            "https://en.wikipedia.org/wiki/test",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(3, games.Count);
        Assert.Contains(games, game => game.SourceHomeTeamId == "wiki-team:team-alpha" && game.SourceAwayTeamId == "wiki-team:team-beta" && game.HomeScore == 80 && game.AwayScore == 70);
    }

    [Fact]
    public void ParsesTodor66CompactDatesAndReversesReturnLeg()
    {
        var warnings = new List<string>();
        var games = WikipediaFibaEuropeanChampionsCupParser.ParseTodor66Games(
            """
            <html><body><table>
              <tr><td>date</td><td>team 1</td><td>country</td><td>scores</td><td>team 2</td><td>country</td></tr>
              <tr><td>16.10,5</td><td>Team Alpha</td><td>ITA</td><td>80-70</td><td>90-85</td><td>Team Beta</td><td>ESP</td></tr>
              <tr><td>3,6</td><td>Team Alpha</td><td>ITA</td><td>75-68</td><td>81-79</td><td>Team Gamma</td><td>GRE</td></tr>
            </table></body></html>
            """,
            "1980-1981",
            "http://todor66.com/basketball/Eurocups/Men_CC_1981.html",
            DateTime.UtcNow,
            "123",
            warnings);

        Assert.Equal(4, games.Count);
        Assert.Contains(games, game => game.GameDateTimeUtc == new DateTime(1980, 10, 16, 0, 0, 0, DateTimeKind.Utc) && game.HomeScore == 80 && game.AwayScore == 70);
        Assert.Contains(games, game => game.GameDateTimeUtc == new DateTime(1980, 11, 5, 0, 0, 0, DateTimeKind.Utc) && game.SourceHomeTeamId == "wiki-team:team-beta" && game.HomeScore == 85 && game.AwayScore == 90);
    }
}
