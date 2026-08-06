using System.Net;
using System.Net.Http;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BasketballDatabaseBalticLeagueDataProviderTests
{
    [Fact]
    public async Task ParsesHistoricalBalticLeagueSchedule()
    {
        var handler = new FixtureHandler("""
            <html><body><table id="tbl4"><thead><tr>
            <th>date</th><th>type</th><th>home</th><th>visitor</th><th>result</th>
            </tr></thead><tbody><tr>
            <td>02 Oct 2007</td><td>Regular</td>
            <td><a href="https://basketball-database.com.court-side.com/csgc/team/home/97511">Home Club</a></td>
            <td><a href="https://basketball-database.com.court-side.com/csgc/team/away/97514">Away Club</a></td>
            <td><a href="https://basketball-database.com.court-side.com/csgc/games/1234088">80 - 72 OT</a></td>
            </tr></tbody></table></body></html>
            """);
        var provider = new BasketballDatabaseBalticLeagueDataProvider(new HttpClient(handler));
        var league = await provider.ResolveLeagueAsync("Europe", "Baltic League", new BackfillExecutionContext(0, 0), CancellationToken.None);

        var result = await provider.GetGamesAsync(league!, "2007-2008", new BackfillExecutionContext(1, 0), CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("Home Club", game.HomeTeamName);
        Assert.Equal((short)80, game.HomeScore);
        Assert.Equal((short)72, game.AwayScore);
        Assert.Equal("Regular", game.CompetitionPhase);
        Assert.Equal("bdb-1234088", game.SourceGameId);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task RejectsUnsupportedHistoricalSeasonWithoutARequest()
    {
        var handler = new FixtureHandler("<html />");
        var provider = new BasketballDatabaseBalticLeagueDataProvider(new HttpClient(handler));
        var result = await provider.GetGamesAsync(
            new BasketballProviderLeague(BasketballDatabaseBalticLeagueDataProvider.Source, "baltic", "Baltic League", null, "literal"),
            "2003-2004",
            new BackfillExecutionContext(1, 0),
            CancellationToken.None);

        Assert.Empty(result.Games);
        Assert.Equal(0, handler.RequestCount);
        Assert.Contains("does not include 2003-2004", Assert.Single(result.Warnings));
    }

    private sealed class FixtureHandler(string content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}

