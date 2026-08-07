using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class WikipediaLithuanianCupBasketballDataProviderTests
{
    [Fact]
    public void ParsesPublishedFinalFourResults()
    {
        var html = """
            <table class="wikitable">
              <tr><th>Season</th></tr>
              <tr><td>2009</td><td>Panevezys</td><td><b>Lietuvos Rytas</b></td><td>84–82</td><td>Zalgiris</td><td>Siauliai</td><td>97–89</td><td>Aisciai</td><td>23–24 January 2009</td></tr>
            </table>
            """;

        var games = WikipediaLithuanianCupBasketballDataProvider.ParseSeason(
            html,
            "2008-2009",
            2008,
            DateTime.UtcNow,
            out var warnings);

        Assert.Equal(2, games.Count);
        Assert.Contains(games, game => game.CompetitionRound == "Final" && game.HomeScore == 84 && game.AwayScore == 82);
        Assert.Contains(games, game => game.CompetitionRound == "Third-place" && game.HomeScore == 97 && game.AwayScore == 89);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ParsesMergedThirdPlaceRowAndTwoLegFinal()
    {
        var html = """
            <table class="wikitable">
              <tr><td>2012–13</td><td>Pasvalys/Prienai</td><td><b>Prienai</b></td><td>77–75<br/>81–60</td><td>Pieno zvaigzdes</td><td colspan="3">Nevezis and Siauliai<br/>(No 3rd place match)</td><td>15–28 March 2013 (Finals)</td></tr>
            </table>
            """;

        var games = WikipediaLithuanianCupBasketballDataProvider.ParseSeason(
            html,
            "2012-2013",
            2012,
            DateTime.UtcNow,
            out var warnings);

        Assert.Equal(2, games.Count);
        Assert.All(games, game => Assert.Equal("Final", game.CompetitionRound));
        Assert.Empty(warnings);
    }
}
