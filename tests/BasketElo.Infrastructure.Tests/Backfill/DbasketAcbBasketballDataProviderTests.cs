using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class DbasketAcbBasketballDataProviderTests
{
    [Fact]
    public void ParsesDbasketGameBlock()
    {
        const string html = """
            <div class="jornada-piece">
              <ul class="round-header">
                <li class="round-header-item-highlight-two">07-10-2007</li>
                <li><table class="resultado"><tr>
                  <td><img alt="MMT Estudiantes" /></td><td class="celda-resultado-1">69</td>
                  <td class="celda-resultado">64</td><td><img alt="Unicaja" /></td>
                </tr></table></li>
                <li><form action="/seasons/acb/2007-08/1/07102007ESTMAL"></form></li>
              </ul>
            </div>
            """;

        var parsed = DbasketAcbBasketballDataProvider.TryParseGame(
            html,
            "2007-2008",
            "Liga Regular",
            "https://dbasket.net/seasons/acb/2007-08/1",
            out var game,
            out var warning);

        Assert.True(parsed, warning);
        Assert.NotNull(game);
        Assert.Equal("MMT Estudiantes", game!.HomeTeamName);
        Assert.Equal("Unicaja", game.AwayTeamName);
        Assert.Equal((short)69, game.HomeScore);
        Assert.Equal((short)64, game.AwayScore);
        Assert.Equal("seasons/acb/2007-08/1/07102007ESTMAL", game.SourceGameId);
        Assert.Equal(DbasketAcbBasketballDataProvider.ParserVersion, game.Provenance!.ParserVersion);
    }

    [Fact]
    public async Task ReadsSeasonRoundsAndGames()
    {
        var handler = new DbasketHandler();
        var provider = new DbasketAcbBasketballDataProvider(
            new HttpClient(handler),
            Options.Create(new DbasketOptions { NetworkAccessEnabled = true, MinRequestIntervalMilliseconds = 0 }));
        var league = await provider.ResolveLeagueAsync("Spain", "ACB", new BackfillExecutionContext(0, 0), CancellationToken.None);

        var result = await provider.GetGamesAsync(
            league!,
            "2007-2008",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        Assert.Single(result.Games);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("MMT Estudiantes", result.Games.Single().HomeTeamName);
    }

    private sealed class DbasketHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var html = request.RequestUri!.AbsolutePath.EndsWith("/2007-08", StringComparison.Ordinal)
                ? "<table><tr><td><a href='/seasons/acb/2007-08/1'>Jornada 1</a></td><td>Liga Regular</td></tr></table>"
                : """
                    <div class="jornada-piece"><ul class="round-header">
                      <li class="round-header-item-highlight-two">07-10-2007</li>
                      <li><table class="resultado"><tr>
                        <td><img alt="MMT Estudiantes" /></td><td class="celda-resultado-1">69</td>
                        <td class="celda-resultado">64</td><td><img alt="Unicaja" /></td>
                      </tr></table></li>
                      <li><form action="/seasons/acb/2007-08/1/07102007ESTMAL"></form></li>
                    </ul></div>
                    """;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            });
        }
    }
}
