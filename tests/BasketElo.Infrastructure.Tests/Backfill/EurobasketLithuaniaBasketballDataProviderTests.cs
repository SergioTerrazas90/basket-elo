using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class EurobasketLithuaniaBasketballDataProviderTests
{
    [Fact]
    public void ParsesCompleteLegacyLklScheduleAndPreservesHomeAwayIds()
    {
        var html = """
            <html><select id="select11"><option>Finals: Game 1</option><option>Regular Season: Round 1</option></select>
            <script>
            var thetext11=new Array()
            thetext11[0] = '<table><tr><td>May. 1</td><td align=right>Zalgiris</td><td><a href="https://www.eurobasket.com/Basketball-Box-Score.aspx?Game=2009_0501_183_683-Lithuania">80-75</a></td><td>Lietuvos</td></tr></table>'
            thetext11[1] = '<table><tr><td>Oct. 9</td><td>Alytus Alita</td><td><a href="https://www.eurobasket.com/Basketball-Box-Score.aspx?Game=2008_1009_1531_183-Lithuania">94-107</a></td><td>Zalgiris</td></tr></table>'
            </script></html>
            """;

        var games = EurobasketLithuaniaBasketballDataProvider.ParseLegacySchedule(
            html,
            "2008-2009",
            "https://example.test/lkl",
            DateTime.UtcNow,
            out var warnings);

        Assert.Equal(2, games.Count);
        var game = games.Single(x => x.SourceGameId.EndsWith("2008_1009_1531_183", StringComparison.Ordinal));
        Assert.Equal("1531", game.SourceHomeTeamId);
        Assert.Equal("183", game.SourceAwayTeamId);
        Assert.Equal((short)94, game.HomeScore);
        Assert.Equal((short)107, game.AwayScore);
        Assert.Equal("Regular season", game.CompetitionPhase);
        Assert.Empty(warnings);
    }
}
