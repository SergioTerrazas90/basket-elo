using BasketElo.Infrastructure.Backfill;
using HtmlAgilityPack;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class OfficialIsraelBasketballDataProviderTests
{
    [Fact]
    public void ParsesBoardOptionsAndOfficialResultRow()
    {
        const string html = """
            <select id="Board">
              <option value="0">Select Comp.</option>
              <option value="5" selected="selected">Winner League</option>
              <option value="17">Winner League Final Series</option>
              <option value="10">Winner Cup</option>
            </select>
            <table class="stats_tbl results">
              <tr class="row odd">
                <td>24/04/1953<div class="mobileOnly"></div></td>
                <td></td><td></td><td>-</td><td></td>
                <td><a href="team.asp?TeamId=590&amp;lang=en"><div class="game_item mid deskOnly">Maccabi North Tel Aviv</div></a></td>
                <td><a href="team.asp?TeamId=570&amp;lang=en"><div class="game_item mid deskOnly">Hapoel Geva</div></a></td>
                <td></td>
                <td><a href="game-zone.asp?GameId=16166&amp;lang=en">20 - 35</a></td>
              </tr>
            </table>
            """;

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var boards = OfficialIsraelBasketballDataProvider.ParseBoardOptions(document);
        var games = OfficialIsraelBasketballDataProvider.ParseGames(
            html,
            "1953-1954",
            "https://basket.co.il/results.asp?cYear=1954&lang=en",
            DateTime.UtcNow,
            "revision",
            "league",
            Assert.Single(boards, board => board.Value == "5"));

        Assert.Equal(3, boards.Count);
        var game = Assert.Single(games);
        Assert.Equal("16166", game.SourceGameId);
        Assert.Equal("Maccabi North Tel Aviv", game.HomeTeamName);
        Assert.Equal("Hapoel Geva", game.AwayTeamName);
        Assert.Equal((short)20, game.HomeScore);
        Assert.Equal((short)35, game.AwayScore);
        Assert.Equal("official-israel-team:590", game.SourceHomeTeamId);
        Assert.Equal("Regular Season", game.CompetitionPhase);
    }
}
