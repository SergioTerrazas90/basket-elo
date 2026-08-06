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

    [Fact]
    public async Task UsesEuroBasket2005EventPageButKeepsOnlyQualificationRounds()
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
            "2005",
            new BackfillExecutionContext(1, 0),
            CancellationToken.None);

        Assert.Equal(3, result.Games.Count);
        Assert.All(result.Games, game =>
            Assert.Contains(
                game.CompetitionPhase,
                new[]
                {
                    "Qualifying Round",
                    "Additional Qualifying Round Games",
                    "Additional Qualifying Tournament"
                }));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ResolvesEuropeanChampionsCupUsingEndYearEditionAndParsesNumericGameLink()
    {
        var handler = new EuropeanFixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "FIBA European Champions Cup",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "1958-1959",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("9001", game.SourceGameId);
        Assert.Equal("RIGA", game.SourceHomeTeamId);
        Assert.Equal("ACAD", game.SourceAwayTeamId);
        Assert.Equal((short)86, game.HomeScore);
        Assert.Equal((short)73, game.AwayScore);
        Assert.Equal("1959:/en/history/112-fiba-mens-european-club-competitions-tier-1/9089/games", game.Provenance?.SourceSeasonKey);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ResolvesSaportaCupUsingTierTwoHistoryFamily()
    {
        var handler = new SaportaFixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "FIBA Saporta Cup",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "1967-1968",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("7001", game.SourceGameId);
        Assert.Equal("VARES", game.SourceHomeTeamId);
        Assert.Equal("MACC", game.SourceAwayTeamId);
        Assert.Equal((short)77, game.HomeScore);
        Assert.Equal((short)67, game.AwayScore);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ResolvesPostSaportaTierTwoUsingASeparateHistoryVariant()
    {
        var provider = new FibaBasketballDataProvider(new HttpClient
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "FIBA European Tier 2",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        Assert.NotNull(league);
        Assert.Equal(
            "212-fiba-mens-european-club-competitions-tier-2|post-saporta",
            league.SourceLeagueId);
    }

    [Fact]
    public async Task ResolvesKoracCupUsingEuroCupChallengeHistoryFamily()
    {
        var handler = new KoracFixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "FIBA Korac Cup",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "1971-1972",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("7002", game.SourceGameId);
        Assert.Equal("LOKO", game.SourceHomeTeamId);
        Assert.Equal("BEOG", game.SourceAwayTeamId);
        Assert.Equal((short)83, game.HomeScore);
        Assert.Equal((short)71, game.AwayScore);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ParsesHistoricEmbeddedGameWhenFibaTeamCodesAreMissing()
    {
        var handler = new HistoricEmbeddedFixtureHandler();
        var provider = new FibaBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://www.fiba.basketball")
        });

        var league = await provider.ResolveLeagueAsync(
            "Europe",
            "FIBA European Champions Cup",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "1995-1996",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("21213", game.SourceGameId);
        Assert.Equal("FIBA:29381", game.SourceHomeTeamId);
        Assert.Equal("Vita", game.HomeTeamName);
        Assert.Equal("FIBA:28445", game.SourceAwayTeamId);
        Assert.Equal("BC Zalgiris", game.AwayTeamName);
        Assert.Equal((short)70, game.HomeScore);
        Assert.Equal((short)78, game.AwayScore);
        Assert.Equal("Preliminary Round I", game.CompetitionPhase);
        Assert.Equal(2, handler.RequestCount);
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? request.RequestUri.AbsolutePath.Contains("208-fiba-eurobasket/2725", StringComparison.OrdinalIgnoreCase)
                    ? EuroBasket2005GamesHtml
                    : request.RequestUri.AbsolutePath.Contains("world-cup-2027-americas", StringComparison.OrdinalIgnoreCase)
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

        private const string EuroBasket2005GamesHtml = """
            <script>
            {"gameId":200501,"teamA":{"code":"CRO","officialName":"Croatia"},"teamB":{"code":"RUS","officialName":"Russia"},"teamAScore":86,"teamBScore":63,"gameDateTimeUTC":"2004-09-08T17:45:00","round":{"roundCode":"QR","roundName":"Qualifying Round"}}
            {"gameId":200502,"teamA":{"code":"LAT","officialName":"Latvia"},"teamB":{"code":"ISR","officialName":"Israel"},"teamAScore":80,"teamBScore":57,"gameDateTimeUTC":"2004-09-08T18:00:00","round":{"roundCode":"AQG","roundName":"Additional Qualifying Round Games"}}
            {"gameId":200503,"teamA":{"code":"CZE","officialName":"Czechia"},"teamB":{"code":"POL","officialName":"Poland"},"teamAScore":82,"teamBScore":57,"gameDateTimeUTC":"2004-09-08T19:00:00","round":{"roundCode":"AQT","roundName":"Additional Qualifying Tournament"}}
            {"gameId":200504,"teamA":{"code":"GER","officialName":"Germany"},"teamB":{"code":"FRA","officialName":"France"},"teamAScore":74,"teamBScore":68,"gameDateTimeUTC":"2005-09-22T19:00:00","round":{"roundCode":"PR","roundName":"Preliminary Round"}}
            {"gameId":200505,"teamA":{"code":"DEN","officialName":"Denmark"},"teamB":{"code":"IRL","officialName":"Ireland"},"teamAScore":86,"teamBScore":70,"gameDateTimeUTC":"2005-09-23T19:00:00","round":{"roundCode":"QG","roundName":"Qualification Games"}}
            </script>
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

    private sealed class EuropeanFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? GamesHtml
                : HistoryHtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }

        private const string HistoryHtml = """
            <table><tbody>
              <tr><td>1959</td><td><a href="/en/history/112-fiba-mens-european-club-competitions-tier-1/9089">1959</a></td></tr>
            </tbody></table>
            """;

        private const string GamesHtml = """
            <div class="date"><div>4 November 1958</div></div>
            <div class="games"><div data-testid="ui-game-card">
              <a href="/en/history/112-fiba-mens-european-club-competitions-tier-1/9089/games/9001">
                <div>Final Â· Finals</div><div>Final</div>
                <div class="wa01avm"><div class="wa01avq">RIGA</div><div class="wa01avo">86</div></div>
                <div class="wa01avm"><div class="wa01avq">ACAD</div><div class="wa01avo">73</div></div>
              </a>
            </div></div>
            """;
    }

    private sealed class HistoricEmbeddedFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? GamesHtml
                : HistoryHtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }

        private const string HistoryHtml = """
            <table><tbody>
              <tr><td>1996</td><td><a href="/en/history/112-fiba-mens-european-club-competitions-tier-1/2163">1996</a></td></tr>
            </tbody></table>
            """;

        private const string GamesHtml = """
            <script>
            {"gameId":21213,"teamA":{"teamId":29381,"organisationId":778,"code":null,"officialName":"\"VITA\" TBILISI","shortName":"Vita"},"teamB":{"teamId":28445,"organisationId":668,"code":null,"officialName":"BC ZALGIRIS KAUNAS","shortName":"BC Zalgiris"},"teamAScore":70,"teamBScore":78,"gameDateTimeUTC":"1995-09-07T00:00:00","round":{"roundCode":"PR1","roundName":"Preliminary Round I"}}
            </script>
            """;
    }

    private sealed class SaportaFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? GamesHtml
                : HistoryHtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }

        private const string HistoryHtml = """
            <table><tbody>
            <tr><td>1968</td><td><a href="/en/history/212-fiba-mens-european-club-competitions-tier-2/8795">1968</a></td></tr>
            </tbody></table>
            """;

        private const string GamesHtml = """
            <div class="date"><div>4 April 1968</div></div>
            <div class="games"><div data-testid="ui-game-card">
            <a href="/en/history/212-fiba-mens-european-club-competitions-tier-2/8795/games/7001-VARES-MACC">
                <div>Final Ã‚Â· Finals</div><div>Final</div>
                <div class="wa01avm"><div class="wa01avq">VARES</div><div class="wa01avo">77</div></div>
                <div class="wa01avm"><div class="wa01avq">MACC</div><div class="wa01avo">67</div></div>
              </a>
            </div></div>
            """;
    }

    private sealed class KoracFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var content = request.RequestUri?.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) == true
                ? GamesHtml
                : HistoryHtml;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }

        private const string HistoryHtml = """
            <table><tbody>
            <tr><td>1972</td><td><a href="/en/history/164-eurocup-challenge/8600">1972</a></td></tr>
            </tbody></table>
            """;

        private const string GamesHtml = """
            <div class="date"><div>7 March 1972</div></div>
            <div class="games"><div data-testid="ui-game-card">
            <a href="/en/history/164-eurocup-challenge/8600/games/7002-LOKO-BEOG">
                <div>Final Â· Finals</div><div>Finals</div>
                <div class="wa01avm"><div class="wa01avq">LOKO</div><div class="wa01avo">83</div></div>
                <div class="wa01avm"><div class="wa01avq">BEOG</div><div class="wa01avo">71</div></div>
              </a>
            </div></div>
            """;
    }
}
