using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class GreekOfficialBasketballDataProviderTests
{
    [Fact]
    public void EsakeRoundParsesStableGameIdGreekDateAndTeams()
    {
        const string html = """
            <div class="esake-program-game">
              <a href="/el/action/EsakegameView?idgame=ABC123"></a>
              <div class="esake-program-game-info">ΚΥΡΙΑΚΗ 14 ΟΚΤΩΒΡΙΟΥ 2007</div>
              <div class="esake-program-game-final-score">
                <div><img src="/esaketeam/00000001/logo.png" />ΠΑΝΑΘΗΝΑΪΚΟΣ ΑΟ</div>
                <div>82 - 74</div>
                <div><img src="/esaketeam/00000002/logo.png" />ΑΕΚ</div>
              </div>
            </div>
            """;

        var game = Assert.Single(GreekOfficialBasketballDataProvider.ParseEsakeRound(
            html, "2007-2008", 2007, "Regular Season", "01", "https://www.esake.gr/el/action/EsakeResults"));

        Assert.Equal("esake:ABC123", game.SourceGameId);
        Assert.Equal(new DateTime(2007, 10, 14, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Panathinaikos", game.HomeTeamName);
        Assert.Equal("AEK Athens", game.AwayTeamName);
        Assert.Equal((short)82, game.HomeScore);
        Assert.Equal("Round 1", game.CompetitionRound);
    }

    [Fact]
    public void EokLegacyPageSkipsAdministrativeTwentyZeroResult()
    {
        const string html = """
            <p>Α ΦΑΣΗ<br>12/09/1999<br>1&nbsp;&nbsp;ΑΕΚ&nbsp;&nbsp;ΑΡΗΣ&nbsp;&nbsp;80-70<br>2&nbsp;&nbsp;ΠΑΟΚ&nbsp;&nbsp;ΔΑΦΝΗ&nbsp;&nbsp;20-0</p>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "1999-2000", 1753, "https://www.basket.gr/cup-men/example", "fixture");

        var game = Assert.Single(result.Games);
        Assert.Equal("AEK Athens", game.HomeTeamName);
        Assert.Equal("Aris", game.AwayTeamName);
        Assert.Equal(new DateTime(1999, 9, 12, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Contains(result.Warnings, warning => warning.Contains("20-0", StringComparison.Ordinal));
    }

    [Fact]
    public void EokTablePageParsesDateTeamsAndScore()
    {
        const string html = """
            <h3>ΗΜΙΤΕΛΙΚΟΙ</h3>
            <table><tbody><tr><td>23/03/2008</td><td>44</td><td>ΟΛΥΜΠΙΑΚΟΣ</td><td>ΠΑΝΑΘΗΝΑΪΚΟΣ</td><td>81-79</td></tr></tbody></table>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "2007-2008", 1765, "https://www.basket.gr/cup-men/example", "fixture");

        var game = Assert.Single(result.Games);
        Assert.Equal("Olympiacos", game.HomeTeamName);
        Assert.Equal("Panathinaikos", game.AwayTeamName);
        Assert.Equal("Semifinals", game.CompetitionRound);
        Assert.Equal("eok-cup:1765:44", game.SourceGameId);
    }

    [Fact]
    public void EokJanuaryHeadingWithPreviousYearTypoStaysInsideEditionSeason()
    {
        const string html = """
            <p>ΠΡΟΗΜΙΤΕΛΙΚΟΙ 19/1/1999<br>34&nbsp;&nbsp;ΗΡΑΚΛΗΣ&nbsp;&nbsp;ΑΡΗΣ&nbsp;&nbsp;65-55</p>
            """;

        var result = GreekOfficialBasketballDataProvider.ParseEokCup(
            html, "1999-2000", 1753, "https://www.basket.gr/cup-men/example", "fixture");

        Assert.Equal(new DateTime(2000, 1, 19, 12, 0, 0, DateTimeKind.Utc), Assert.Single(result.Games).GameDateTimeUtc);
    }
}
