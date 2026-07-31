using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class AcbArchiveBasketballDataProviderTests
{
    [Fact]
    public void ParsesLegacyAcbGamePage()
    {
        const string html = """
            <div class="titulopartido"><table>
              <tr><td>MMT ESTUDIANTES |</td><td>&nbsp;UNICAJA</td></tr>
              <tr><td><font>69 |</font></td><td><font>&nbsp;64</font></td></tr>
            </table></div>
            <table class="estadisticas"><tr><td>&nbsp;J 1 | 07/10/2007 | 12:30 | Madrid Arena</td></tr></table>
            """;

        var parsed = AcbArchiveBasketballDataProvider.TryParseGame(
            html,
            "LACB52001",
            "2007-2008",
            "https://web.archive.org/web/20140530192853id_/http://www.acb.com:80/fichas/LACB52001.php",
            "20140530192853",
            out var game,
            out var warning);

        Assert.True(parsed, warning);
        Assert.NotNull(game);
        Assert.Equal("MMT ESTUDIANTES", game!.HomeTeamName);
        Assert.Equal("UNICAJA", game.AwayTeamName);
        Assert.Equal((short)69, game.HomeScore);
        Assert.Equal((short)64, game.AwayScore);
        Assert.Equal(new DateTime(2007, 10, 7, 12, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal(AcbArchiveBasketballDataProvider.ParserVersion, game.Provenance!.ParserVersion);
    }

    [Fact]
    public async Task ReadsAvailabilityAndReplayPages()
    {
        var handler = new AcbHandler();
        var provider = new AcbArchiveBasketballDataProvider(
            new HttpClient(handler),
            Options.Create(new AcbArchiveOptions { NetworkAccessEnabled = true, MinRequestIntervalMilliseconds = 0 }));
        var league = await provider.ResolveLeagueAsync("Spain", "ACB", new BackfillExecutionContext(0, 0), CancellationToken.None);

        var result = await provider.GetGamesAsync(
            league!,
            "2007-2008",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        Assert.Single(result.Games);
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(result.Games, x => x.SourceGameId == "LACB52001");
    }

    private sealed class AcbHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.RequestUri!.AbsoluteUri.Contains("archive.org/wayback/available", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"archived_snapshots\":{\"closest\":{\"available\":true,\"timestamp\":\"20140530192853\"}}}")
                });
            }

            var html = """
                <div class="titulopartido"><table>
                  <tr><td>MMT ESTUDIANTES |</td><td>&nbsp;UNICAJA</td></tr>
                  <tr><td><font>69 |</font></td><td><font>&nbsp;64</font></td></tr>
                </table></div>
                <table class="estadisticas"><tr><td>&nbsp;J 1 | 07/10/2007 | 12:30 | Madrid Arena</td></tr></table>
                """;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            });
        }
    }
}
