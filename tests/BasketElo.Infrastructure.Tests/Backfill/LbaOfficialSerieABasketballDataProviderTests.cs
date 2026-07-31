using System.Net;
using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public sealed class LbaOfficialSerieABasketballDataProviderTests
{
    [Fact]
    public async Task StopsCleanlyWhenDiagnosticRequestBudgetIsReached()
    {
        var handler = new LbaHandler();
        var provider = new LbaOfficialSerieABasketballDataProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://www.legabasket.it") },
            Options.Create(new LbaOfficialOptions { MinRequestIntervalMilliseconds = 0 }));
        var context = new BackfillExecutionContext(1, 0);
        var league = await provider.ResolveLeagueAsync("Italy", "Serie A", context, CancellationToken.None);

        var result = await provider.GetGamesAsync(
            league!,
            "2007-2008",
            context,
            CancellationToken.None);

        Assert.Empty(result.Games);
        Assert.Equal(1, handler.RequestCount);
        Assert.Contains(result.Warnings, warning => warning.Contains("request budget", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReadsRegularSeasonAndPlayoffsWithStableClubIdentities()
    {
        var handler = new LbaHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://www.legabasket.it") };
        var provider = new LbaOfficialSerieABasketballDataProvider(
            client,
            Options.Create(new LbaOfficialOptions { MinRequestIntervalMilliseconds = 0 }));
        var context = new BackfillExecutionContext(0, 0);
        var league = await provider.ResolveLeagueAsync(
            "Italy",
            "Serie A",
            context,
            CancellationToken.None);

        var result = await provider.GetGamesAsync(
            league!,
            "2007-2008",
            context,
            CancellationToken.None);

        Assert.Equal(6, handler.RequestCount);
        Assert.Equal(2, result.Games.Count);
        Assert.Empty(result.Warnings);
        Assert.Collection(
            result.Games,
            regularSeason =>
            {
                Assert.Equal("Regular Season", regularSeason.CompetitionPhase);
                Assert.Equal("club:101", regularSeason.SourceHomeTeamId);
                Assert.Equal("club:202", regularSeason.SourceAwayTeamId);
                Assert.Equal((short)81, regularSeason.HomeScore);
                Assert.Equal((short)75, regularSeason.AwayScore);
            },
            playoffs =>
            {
                Assert.Equal("Play Off", playoffs.CompetitionPhase);
                Assert.Equal("club:202", playoffs.SourceHomeTeamId);
                Assert.Equal("club:101", playoffs.SourceAwayTeamId);
                Assert.Equal("Quarter-finals", playoffs.CompetitionRound);
            });
        Assert.All(result.Games, game =>
        {
            Assert.Equal(LbaOfficialSerieABasketballDataProvider.Source, game.Source);
            Assert.Equal("IT", game.SourceHomeTeamCountryCode);
            Assert.Equal("IT", game.SourceAwayTeamCountryCode);
            Assert.Equal(LbaOfficialSerieABasketballDataProvider.ParserVersion, game.Provenance!.ParserVersion);
        });
    }

    private sealed class LbaHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var uri = request.RequestUri!;
            var json = uri.AbsolutePath switch
            {
                "/api/championships/get-championships" => """
                    {"competitions":[
                      {"id":195,"year":2007,"ctype_code":"RS","ctype_name":"Regular Season","full_name":"Serie A 2007-08"},
                      {"id":196,"year":2007,"ctype_code":"PO","ctype_name":"Play Off","full_name":"Play Off 2007-08"}
                    ]}
                    """,
                "/api/teams/get-teams" => """
                    {"teams":[
                      {"id":11,"club_id":101,"club_code":"MIL","name":"Armani Jeans Milano"},
                      {"id":22,"club_id":202,"club_code":"SIE","name":"Montepaschi Siena"}
                    ]}
                    """,
                "/api/championships/get-championships-calendar-by-id"
                    when !uri.Query.Contains("&d=", StringComparison.Ordinal) =>
                    """{"filters":{"days":[{"event_serial":1,"name":"Round 1"}]}}""",
                "/api/championships/get-championships-calendar-by-id"
                    when uri.Query.Contains("id=195", StringComparison.Ordinal) => """
                    {"matches":[{
                      "id":9001,"match_datetime":"2007-09-30T18:15:00+02:00",
                      "h_team_id":11,"h_team_name":"Armani Jeans Milano",
                      "v_team_id":22,"v_team_name":"Montepaschi Siena",
                      "home_final_score":81,"visitor_final_score":75,"day_name":"Round 1"
                    }]}
                    """,
                "/api/championships/get-championships-calendar-by-id" => """
                    {"matches":[{
                      "id":9002,"match_datetime":"2008-05-15T20:30:00+02:00",
                      "h_team_id":22,"h_team_name":"Montepaschi Siena",
                      "v_team_id":11,"v_team_name":"Armani Jeans Milano",
                      "home_final_score":85,"visitor_final_score":78,"day_name":"Quarter-finals"
                    }]}
                    """,
                _ => throw new InvalidOperationException($"Unexpected official LBA request: {uri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }
}
