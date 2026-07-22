using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using System.Net;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class FibaBasketballDataProviderTests
{
    [Fact]
    public async Task ResolvesEditionAndParsesOfficialGameCard()
    {
        var handler = new FixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "FIBA EuroBasket Qualifiers",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "2022-2023",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("99607", game.SourceGameId);
        Assert.Equal("GEO", game.SourceHomeTeamId);
        Assert.Equal("SUI", game.SourceAwayTeamId);
        Assert.Equal("GEO", game.SourceHomeTeamCountryCode);
        Assert.Equal("SUI", game.SourceAwayTeamCountryCode);
        Assert.Equal((short)96, game.HomeScore);
        Assert.Equal((short)88, game.AwayScore);
        Assert.Equal("Group Phase", game.CompetitionPhase);
        Assert.Equal("Group E", game.CompetitionRound);
        Assert.Equal(new DateTime(2022, 2, 20, 0, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal(FibaBasketballDataProvider.ParserVersion, game.Provenance?.ParserVersion);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ResolvesWorldCup2027EditionFromHistoryAndParsesOfficialGameCard()
    {
        var handler = new FixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "World",
            "FIBA Basketball World Cup Qualifiers",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "2027-2028",
            new BackfillExecutionContext(3, 0),
            CancellationToken.None);

        Assert.Equal(2, result.Games.Count);
        Assert.Contains(result.Games, game => game.SourceGameId == "127002" && game.SourceHomeTeamId == "ESP" && game.SourceAwayTeamId == "GEO");
        Assert.Contains(result.Games, game => game.SourceGameId == "127003" && game.SourceHomeTeamId == "USA" && game.SourceAwayTeamId == "BRA");
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task AddsEmbeddedGamesFromHiddenFibaRoundsWhenCardsShowOnlyOneRound()
    {
        var handler = new EmbeddedFixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Africa",
            "FIBA AfroBasket Qualifiers",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "2017-2018",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        Assert.Equal(2, result.Games.Count);
        Assert.Contains(result.Games, game => game.SourceGameId == "77057" && game.CompetitionPhase == "Zone 1");
        Assert.Contains(result.Games, game => game.SourceGameId == "77098" && game.CompetitionPhase == "Zone 5 - Qualifiers");
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? request.RequestUri.AbsolutePath.Contains("world-cup-2027-americas", StringComparison.OrdinalIgnoreCase)
                    ? AmericasEventGamesHtml
                    : request.RequestUri.AbsolutePath.Contains("world-cup-2027", StringComparison.OrdinalIgnoreCase)
                        ? DirectEventGamesHtml
                        : GamesHtml
                : HistoryHtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }

        private const string HistoryHtml = """
            <table><tbody>
              <tr><td>2022</td><td><a href="/en/history/205-fiba-eurobasket-qualifiers/208147">2022</a></td></tr>
              <tr><td>2027</td><td>
                <a href="/en/events/fiba-basketball-world-cup-2027-european-qualifiers">Europe</a>
                <a href="/en/events/fiba-basketball-world-cup-2027-americas-qualifiers">Americas</a>
              </td></tr>
            </tbody></table>
            """;

        private const string GamesHtml = """
            <div class="date"><div>20 February 2022</div></div>
            <div class="games"><div data-testid="ui-game-card">
              <a href="/en/history/205-fiba-eurobasket-qualifiers/208147/games/99607-GEO-SUI">
                <div>Group Phase &middot; Group E</div><div>Final</div>
                <div class="wa01avm"><div>GEO GEO 96</div></div>
                <div class="wa01avm"><div>SUI SUI 88</div></div>
              </a>
            </div></div>
            """;

        private const string DirectEventGamesHtml = """
            <div class="date"><div>27 November 2025</div></div>
            <div class="games"><div data-testid="ui-game-card">
              <a href="/en/events/fiba-basketball-world-cup-2027-european-qualifiers/games/127002-ESP-GEO">
                <div>Group Phase &middot; Group C</div><div>Final</div>
                <div class="wa01avm"><div>ESP ESP 74</div></div>
                <div class="wa01avm"><div>GEO GEO 64</div></div>
              </a>
            </div></div>
            """;

        private const string AmericasEventGamesHtml = """
            <div class="date"><div>28 November 2025</div></div>
            <div class="games"><div data-testid="ui-game-card">
              <a href="/en/events/fiba-basketball-world-cup-2027-americas-qualifiers/games/127003-USA-BRA">
                <div>Group Phase &middot; Group A</div><div>Final</div>
                <div class="wa01avm"><div>USA USA 80</div></div>
                <div class="wa01avm"><div>BRA BRA 70</div></div>
              </a>
            </div></div>
            """;
    }

    private sealed class EmbeddedFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? GamesWithEmbeddedRoundHtml
                : HistoryHtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }

        private const string HistoryHtml = """
            <table><tbody>
              <tr><td>2017</td><td><a href="/en/history/178-fiba-afrobasket-qualifiers/10662">2017</a></td></tr>
            </tbody></table>
            """;

        private const string GamesWithEmbeddedRoundHtml = """
            <div class="date"><div>16 March 2017</div></div>
            <div class="games"><div data-testid="ui-game-card">
              <a href="/en/history/178-fiba-afrobasket-qualifiers/10662/games/77057-ALG-MAR">
                <div>Zone 1 &middot; Group A</div><div>Final</div>
                <div class="wa01avm"><div>ALG ALG 71</div></div>
                <div class="wa01avm"><div>MAR MAR 76</div></div>
              </a>
            </div></div>
            <script>
            {"gameId":77098,"teamA":{"code":"RWA","officialName":"Rwanda"},"teamB":{"code":"KEN","officialName":"Kenya"},"teamAScore":72,"teamBScore":69,"gameDateTimeUTC":"2017-03-12T12:45:00","round":{"roundCode":"F-QR","roundName":"Zone 5 - Qualifiers"}}
            </script>
            """;
    }
}
