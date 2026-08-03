using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class SerbianHistoricalLegacyParserTests
{
    [Fact]
    public void ParsesPearlBasketRoundDateAndFinalScore()
    {
        const string html = """
            <p class="turno">1. Round</p>
            <p class="data">25-10-1980</p>
            <p class="partita">Partizan - Cibona 82-76 (41-38)</p>
            """;

        var games = SerbianHistoricalLegacyParser.ParsePearlBasket(
            html,
            "1980-1981",
            "https://pearlbasket.altervista.org/JU81.htm",
            value => value,
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        var game = Assert.Single(games);
        Assert.Equal("Partizan", game.HomeTeamName);
        Assert.Equal("Cibona", game.AwayTeamName);
        Assert.Equal((short)82, game.HomeScore);
        Assert.Equal((short)76, game.AwayScore);
        Assert.Equal(new DateTime(1980, 10, 25, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Round 1", game.CompetitionRound);
    }

    [Fact]
    public void SeparatesPearlBasketPlayoffAndPlayOutPhases()
    {
        const string html = """
            <p class="turno">22. Round</p>
            <p class="data">10-04-1991</p>
            <p class="partita">Partizan - Cibona 82-76</p>
            <p class="turno">1/4 Finals</p>
            <p class="partita">Partizan - Cibona 88-78</p>
            <p class="turno">Play Off</p>
            <p class="partita">Partizan - Cibona 90-80</p>
            <p class="turno">Play out</p>
            <p class="partita">Borac - Zadar 75-70</p>
            """;

        var games = SerbianHistoricalLegacyParser.ParsePearlBasket(
            html,
            "1990-1991",
            "https://pearlbasket.altervista.org/JU91.htm",
            value => value,
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        Assert.Equal(4, games.Count);
        Assert.Equal("Regular Season", games[0].CompetitionPhase);
        Assert.Equal("Playoffs", games[1].CompetitionPhase);
        Assert.Equal("Playoffs", games[2].CompetitionPhase);
        Assert.Equal("Play-out", games[3].CompetitionPhase);
    }

    [Fact]
    public void ParsesWikipediaSportsResultsMatrixEntries()
    {
        const string raw = """
            |name_PAR = [[KK Partizan|Partizan Zepter]] | short_PAR = [[KK Partizan|PAR]]
            |name_CZV = [[KK Crvena zvezda|Crvena zvezda]] | short_CZV = [[KK Crvena zvezda|CZV]]
            |match_PAR_CZV=78-86
            |match_CZV_PAR=69-77
            """;

        var games = SerbianHistoricalLegacyParser.ParseWikipediaRaw(
            raw,
            "1997-1998",
            "https://en.wikipedia.org/w/index.php?title=1997&action=raw",
            value => value.Replace(" Zepter", string.Empty, StringComparison.Ordinal),
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        Assert.Equal(2, games.Count);
        Assert.Equal((short)78, games[0].HomeScore);
        Assert.Equal("Partizan", games[0].HomeTeamName);
        Assert.Equal("Crvena zvezda", games[0].AwayTeamName);
    }

    [Fact]
    public void ParsesWikipediaSportsResultsMatrixEntriesWithWhitespaceAfterPipe()
    {
        const string raw = """
            |name_PAR = [[KK Partizan|Partizan]]
            |name_CZV = [[KK Crvena zvezda|Crvena zvezda]]
            | match_PAR_CZV = 78-86
            | match_CZV_PAR = 69-77
            """;

        var games = SerbianHistoricalLegacyParser.ParseWikipediaRaw(
            raw,
            "1991-1992",
            "https://en.wikipedia.org/w/index.php?title=1991&action=raw",
            value => value,
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        Assert.Equal(2, games.Count);
    }

    [Fact]
    public void ParsesSerbianWikipediaRoundResultRows()
    {
        const string raw = """
            == Резултати по колима ==
            === 1.круг ===
            {| class="mw-collapsible"
            |-
            | '''Партизан -''' Ц. звезда||'''89:87'''
            |-
            | '''Борац БЛ''' - Будућност
            | '''75:66'''
            |}
            == Плеј-оф ==
            # Партизан - Црвена звезда 77:83
            """;

        var games = SerbianHistoricalLegacyParser.ParseSerbianWikipediaRoundResults(
            raw,
            "1992-1993",
            "https://sr.wikipedia.org/wiki/example.",
            value => value switch
            {
                "Partizan" => "Partizan",
                "Crvena zvezda" => "Crvena zvezda",
                "Borac Banja Luka" => "Borac Banja Luka",
                "Budućnost" => "Budućnost",
                "Ц. звезда" => "Crvena zvezda",
                _ => value
            },
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        Assert.Equal(2, games.Count);
        Assert.Equal("Round 1", games[0].CompetitionRound);
        Assert.Equal((short)89, games[0].HomeScore);
        Assert.Equal((short)66, games[1].AwayScore);
    }

    [Fact]
    public void ParsesBorbaOcrTeamScoreLine()
    {
        const string html = "<div id=\"text\"><p>YUBA LIGA: Crvena zvezda — Radnički 112:84.</p></div>";

        var games = SerbianHistoricalLegacyParser.ParseBorbaText(
            html,
            "1992-1993",
            "https://pretraziva.rs/show/borba/1993-01-18/24",
            new DateTime(1993, 1, 18, 12, 0, 0, DateTimeKind.Utc),
            value => value switch
            {
                "CRVENAZVEZDA" => "Crvena zvezda",
                "RADNICKI" => "Radnički",
                _ => value
            },
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        var game = Assert.Single(games);
        Assert.Equal("Crvena zvezda", game.HomeTeamName);
        Assert.Equal("Radnički", game.AwayTeamName);
        Assert.Equal((short)112, game.HomeScore);
        Assert.Equal((short)84, game.AwayScore);
    }

    [Fact]
    public void ParsesBorbaOcrCompactRoundSummaryWithoutTeamSeparator()
    {
        const string html = "<div id=\"text\"><p>Прво коло: Војводина Колубара 87:68, Спартак ОКК ТГ Боровица 86:70.</p></div>";

        var games = SerbianHistoricalLegacyParser.ParseBorbaText(
            html,
            "1994-1995",
            "https://pretraziva.rs/show/borba/1994-10-03/21",
            new DateTime(1994, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            value => value switch
            {
                "VOJVODINA" => "Vojvodina",
                "KOLUBARA" => "Kolubara",
                "SPARTAK" => "Spartak",
                "BOROVICA" => "Borovica",
                _ => value
            },
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.HomeTeamName == "Vojvodina" && game.AwayTeamName == "Kolubara" && game.HomeScore == 87 && game.AwayScore == 68);
        Assert.Contains(games, game => game.HomeTeamName == "Spartak" && game.AwayTeamName == "Borovica" && game.HomeScore == 86 && game.AwayScore == 70);
    }

    [Fact]
    public void ParsesVerifiedBorbaPlayoffResultWithCyrillicOcr()
    {
        const string html = "<div id=\"text\"><p>РАБОТНИЧКИ — ЦРВЕНА</p><p>ЗВЕЗДА 85:82 (49:44)</p></div>";

        var games = SerbianHistoricalLegacyParser.ParseBorbaVerifiedResult(
            html,
            "1991-1992",
            "https://pretraziva.rs/show/borba/1992-04-22/27",
            new DateTime(1992, 4, 21, 12, 0, 0, DateTimeKind.Utc),
            "Rabotnički",
            "Crvena zvezda",
            85,
            82,
            "Semifinal Game 1",
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        var game = Assert.Single(games);
        Assert.Equal(new DateTime(1992, 4, 21, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Playoffs", game.CompetitionPhase);
        Assert.Equal("Semifinal Game 1", game.CompetitionRound);
        Assert.Equal((short)85, game.HomeScore);
        Assert.Equal((short)82, game.AwayScore);
    }

    [Fact]
    public void ParsesPartizanopediaScheduleRow()
    {
        const string html = """
            <h3>Prvenstvo:</h3>
            <table class="utakmice95"><tr class="pobeda">
              <td>0.</td><td>01. 10. 1994.</td><td>Partizan</td><td>90 - 80</td><td>Benfica</td>
            </tr></table>
            <h3>Kup Jugoslavije:</h3>
            <table class="utakmice90"><tr class="pobeda">
              <td>1.</td><td>02. 10. 1994.</td><td>Jugotes</td><td>82 - 91</td><td>Partizan</td>
            </tr></table>
            <h3>Prvenstvo:</h3>
            <table class="utakmice90"><tr class="pobeda">
              <td>2.</td><td>03. 10. 1994.</td><td>Jugotes</td><td>82 - 91</td><td>Partizan</td>
            </tr></table>
            """;

        var games = SerbianHistoricalLegacyParser.ParsePartizanopedia(
            html,
            "1994-1995",
            "https://www.partizanopedia.rs/1994-95%20kosarka.html",
            value => value,
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow);

        var game = Assert.Single(games);
        Assert.Equal("Jugotes", game.HomeTeamName);
        Assert.Equal("Partizan", game.AwayTeamName);
        Assert.Equal((short)82, game.HomeScore);
        Assert.Equal(new DateTime(1994, 10, 3, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
    }

    [Fact]
    public void ParsesPartizanopediaPlayoffsAndCupWhenRequested()
    {
        const string html = """
            <h3>Prvenstvo:</h3>
            <table class="utakmice90"><tr class="pobeda">
              <td>22.</td><td>10. 04. 1992.</td><td>Partizan</td><td>90 - 80</td><td>Sloboda</td>
            </tr></table>
            <h3>Plej - of:</h3>
            <table class="utakmice90"><tr class="pobeda">
              <td>1/2 I</td><td>23. 04. 1992.</td><td>Partizan</td><td>87 - 67</td><td>Sloboda</td>
            </tr></table>
            <h3>Kup Jugoslavije:</h3>
            <table class="utakmice90"><tr class="pobeda">
              <td>1/8</td><td>19. 09. 1991.</td><td>Zorka</td><td>80 - 89</td><td>Partizan</td>
            </tr></table>
            <h3>Liga šampiona:</h3>
            <table class="utakmice90"><tr class="pobeda">
              <td>1</td><td>01. 11. 1991.</td><td>Partizan</td><td>90 - 80</td><td>Komodor</td>
            </tr></table>
            """;

        var games = SerbianHistoricalLegacyParser.ParsePartizanopedia(
            html,
            "1991-1992",
            "https://www.partizanopedia.rs/1991-92%20kosarka.html",
            value => value,
            _ => "RS",
            "serbian-historical",
            DateTime.UtcNow,
            new HashSet<string>(["Playoffs", "Cup"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.CompetitionPhase == "Playoffs" && game.CompetitionRound == "1/2 I");
        Assert.Contains(games, game => game.CompetitionPhase == "Cup" && game.CompetitionRound == "1/8");
    }

    [Fact]
    public void ParsesBorbaSearchTotal()
    {
        const string html = "<p>Results 1-100 from 371 total. Search took 395 milliseconds.</p>";

        Assert.Equal(371, SerbianHistoricalLegacyParser.ParseBorbaSearchTotal(html));
    }
}
