using System.Net;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class GlobalSportsArchiveBasketballDataProviderTests
{
    [Theory]
    [InlineData("Spain", "ACB", "gsa-acb")]
    [InlineData("Europe", "ABA League", "gsa-aba-league")]
    [InlineData("Europe", "BIBL", "gsa-bibl")]
    [InlineData("Europe", "Champions League", "gsa-champions-league")]
    [InlineData("Europe", "Eurocup", "gsa-eurocup")]
    [InlineData("Europe", "Euroleague", "gsa-euroleague")]
    public async Task ResolvesCovidFallbackCompetitions(string country, string leagueName, string sourceLeagueId)
    {
        var provider = new GlobalSportsArchiveBasketballDataProvider(new HttpClient
        {
            BaseAddress = new Uri("https://globalsportsarchive.com")
        });

        var league = await provider.ResolveLeagueAsync(
            country,
            leagueName,
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        Assert.NotNull(league);
        Assert.Equal(sourceLeagueId, league.SourceLeagueId);
        Assert.Equal(leagueName, league.Name);
    }

    [Fact]
    public async Task ResolvesHistoricalAfroBasketAndParsesGameCard()
    {
        var handler = new FixtureHandler();
        var provider = new GlobalSportsArchiveBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://globalsportsarchive.com")
        });

        var league = await provider.ResolveLeagueAsync(
            "Africa",
            "FIBA AfroBasket",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "2003-2004",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("gsa-3581392", game.SourceGameId);
        Assert.Equal("CIV", game.SourceHomeTeamId);
        Assert.Equal("Côte d'Ivoire", game.HomeTeamName);
        Assert.Equal("ALG", game.SourceAwayTeamId);
        Assert.Equal("CIV", game.SourceHomeTeamCountryCode);
        Assert.Equal("ALG", game.SourceAwayTeamCountryCode);
        Assert.Equal((short)85, game.HomeScore);
        Assert.Equal((short)78, game.AwayScore);
        Assert.Equal(new DateTime(2003, 8, 12, 18, 30, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
        Assert.Equal("Group Stage", game.CompetitionPhase);
        Assert.Equal(GlobalSportsArchiveBasketballDataProvider.ParserVersion, game.Provenance?.ParserVersion);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task ResolvesHistoricalAsiaCupAndParsesLegacyAbcGameCard()
    {
        var handler = new AsiaFixtureHandler();
        var provider = new GlobalSportsArchiveBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://globalsportsarchive.com")
        });

        var league = await provider.ResolveLeagueAsync(
            "Asia",
            "FIBA Asia Cup",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "2003-2004",
            new BackfillExecutionContext(2, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("gsa-900001", game.SourceGameId);
        Assert.Equal("CHN", game.SourceHomeTeamId);
        Assert.Equal("China PR", game.HomeTeamName);
        Assert.Equal("PHI", game.SourceAwayTeamId);
        Assert.Equal("CHN", game.SourceHomeTeamCountryCode);
        Assert.Equal("PHI", game.SourceAwayTeamCountryCode);
        Assert.Equal((short)86, game.HomeScore);
        Assert.Equal((short)61, game.AwayScore);
        Assert.Equal("Group Stage", game.CompetitionPhase);
        Assert.Equal("Group Stage", game.CompetitionRound);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task RetainsScorelessGsaFixtureAsEloExcludedGameWithSourceUrl()
    {
        var handler = new ScorelessFixtureHandler();
        var provider = new GlobalSportsArchiveBasketballDataProvider(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://globalsportsarchive.com")
        });

        var league = await provider.ResolveLeagueAsync(
            "Africa",
            "FIBA AfroBasket",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var result = await provider.GetGamesAsync(
            league!,
            "1992-1993",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        var game = Assert.Single(result.Games);
        Assert.Equal("gsa-3581160", game.SourceGameId);
        Assert.Equal("score_pending", game.Status);
        Assert.Null(game.HomeScore);
        Assert.Null(game.AwayScore);
        Assert.Equal("source_missing_final_score", game.ExclusionReason);
        Assert.Equal(
            "https://globalsportsarchive.com/match/basketball/1992-12-31/senegal-vs-central-african-republic/3581160/",
            game.Provenance?.SourceUrl);
        Assert.Contains(result.Warnings, warning => warning.Contains(game.Provenance!.SourceUrl!, StringComparison.Ordinal));
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.AbsolutePath.Contains("/final/") == true ? "<html></html>" : Html)
            });
        }

        private const string Html = """
            <a href="/competition/basketball/fiba-africa-championship-2003-egypt/group-stage/111126/">Group Stage</a>
            <a href="/competition/basketball/fiba-africa-championship-2003-egypt/final/111128/">Final</a>
            <a href="https://globalsportsarchive.com/match/basketball/2003-08-12/cote-divoire-vs-algeria/3581392/" title="match report">
              <div class="gsa-c-match-row"><div class="gsa-c-match-c1">18:30</div>
                <div class="gsa-c-match-c2" onClick="gotoUrl(35667,'cote-divoire');"><span class="gsa-c-team_full">Côte d'Ivoire</span></div>
                <div class="gsa-c-match-c3">85 : 78</div>
                <div class="gsa-c-match-c4" onClick="gotoUrl(35668,'algeria');"><span class="gsa-c-team_full">Algeria</span></div>
              </div>
            </a>
            """;
    }

    private sealed class AsiaFixtureHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri?.AbsolutePath.Contains("/final/") == true ? "<html></html>" : Html)
            });
        }

        private const string Html = """
            <a href="/competition/basketball/abc-championship-2003-china-pr/group-stage/94961/">Group Stage</a>
            <a href="/competition/basketball/abc-championship-2003-china-pr/final/94972/">Final</a>
            <a href="https://globalsportsarchive.com/match/basketball/2003-10-01/china-pr-vs-philippines/900001/" title="match report">
              <div class="legacy-match">
                <span class="team_a_name"><span class="team_full">China PR</span></span>
                <span class="match_score">86 : 61</span>
                <span class="team_b_name"><span class="team_full">Philippines</span></span>
              </div>
            </a>
            """;
    }

    private sealed class ScorelessFixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Html)
            });

        private const string Html = """
            <a href="/competition/basketball/fiba-africa-championship-1992-egypt/group-stage/111085/">Group Stage</a>
            <a href="/competition/basketball/fiba-africa-championship-1992-egypt/final/111087/">Final</a>
            <a href="https://globalsportsarchive.com/match/basketball/1992-12-31/senegal-vs-central-african-republic/3581160/" title="match report">
              <div class="gsa-c-match-row"><div class="gsa-c-match-c1"><span class="kickoff_time">TBD</span></div>
                <div class="gsa-c-match-c2" onClick="gotoUrl(35665,'senegal');"><span class="gsa-c-team_full">Senegal</span></div>
                <div class="gsa-c-match-c3">:</div>
                <div class="gsa-c-match-c4" onClick="gotoUrl(35668,'central-african-republic');"><span class="gsa-c-team_full">Central African Republic</span></div>
              </div>
            </a>
            """;
    }
}
