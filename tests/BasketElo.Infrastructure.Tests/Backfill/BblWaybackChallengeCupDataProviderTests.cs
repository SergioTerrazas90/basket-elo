using System.Net;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class BblWaybackChallengeCupDataProviderTests
{
    [Fact]
    public async Task ReadsTheFullChallengeCupGameSet()
    {
        var handler = new FixtureHandler("""
            <html><body>
            <a href="http://www.bbl.net/index.php/b19sYW5nPWVuJm9fc2Vhcz0xOSZvX2xlYWc9OSZmdXNlYWN0aW9uPXRlYW1zLm1haW4mdD00OA=="><b style="font-size:13px;">Home Club</b></a>
            <a href="http://www.bbl.net/index.php/b19sYW5nPWVuJm9fc2Vhcz0xOSZvX2xlYWc9OSZmdXNlYWN0aW9uPXRlYW1zLm1haW4mdD03Nw=="><b style="font-size:13px;">Away Club</b></a>
            <strong>2007-10-02 | 18:30</strong>
            <div style="font-size:22px;">84 : 75</div>
            </body></html>
            """);
        var provider = new BblWaybackChallengeCupDataProvider(new HttpClient(handler));
        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "Baltic League Challenge Cup",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        var result = await provider.GetGamesAsync(
            league!,
            "2007-2008",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        Assert.Equal(114, result.Games.Count);
        Assert.Equal(115, handler.RequestCount);
        Assert.Empty(result.Warnings);
        Assert.False(result.HasMorePages);
        Assert.Equal("bbl-1137", result.Games.First().SourceGameId);
        Assert.Equal("bbl-1261", result.Games.Last().SourceGameId);
        Assert.Equal("Final", result.Games.Last().CompetitionRound);
    }

    [Fact]
    public async Task StopsWhenTheRequestBudgetIsReached()
    {
        var handler = new FixtureHandler("""
            <html><body>
            <a href="http://www.bbl.net/index.php/b19sYW5nPWVuJm9fc2Vhcz0xOSZvX2xlYWc9OSZmdXNlYWN0aW9uPXRlYW1zLm1haW4mdD00OA=="><b style="font-size:13px;">Home Club</b></a>
            <a href="http://www.bbl.net/index.php/b19sYW5nPWVuJm9fc2Vhcz0xOSZvX2xlYWc9OSZmdXNlYWN0aW9uPXRlYW1zLm1haW4mdD03Nw=="><b style="font-size:13px;">Away Club</b></a>
            <strong>2007-10-02 | 18:30</strong>
            <div style="font-size:22px;">84 : 75</div>
            </body></html>
            """);
        var provider = new BblWaybackChallengeCupDataProvider(new HttpClient(handler));

        var result = await provider.GetGamesAsync(
            new BasketballProviderLeague(
                BblWaybackChallengeCupDataProvider.Source,
                "bbl-challenge-cup",
                "Baltic League Challenge Cup",
                null,
                "literal"),
            "2007-2008",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        Assert.Equal(1, result.Games.Count);
        Assert.Equal(2, handler.RequestCount);
        Assert.True(result.HasMorePages);
        Assert.Contains("request budget", Assert.Single(result.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixtureHandler(string content) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }
}
