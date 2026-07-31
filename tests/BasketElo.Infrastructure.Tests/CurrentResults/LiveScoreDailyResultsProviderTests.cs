using System.Net;
using System.Net.Http.Headers;
using BasketElo.Infrastructure.CurrentResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.CurrentResults;

public class LiveScoreDailyResultsProviderTests
{
    [Fact]
    public async Task FetchAsync_ParsesFinishedAndScheduledGames()
    {
        const string html = """
            <html><body><div class="group">
              <div class="Pa"><span class="Sa">Spain</span><span class="Ta">ACB: Play-off</span></div>
              <div class="Xe"><a href="/basketball/spain/acb/real-madrid-vs-barcelona/1234567/"><span class="Ih">FT</span><div class="nf"><div class="vf">Real Madrid</div><div class="vf">Barcelona</div></div><div class="rf"><span class="hf">88</span><span class="hf">76</span></div></a></div>
              <div class="Pa"><span class="Sa">Europe</span><span class="Ta">Euroleague</span></div>
              <div class="Xe"><button data-eventId="7654321"></button><span class="Ih">20:30</span><div class="nf"><div class="vf">Olympiacos</div><div class="vf">Fenerbahce</div></div><div class="rf"></div></div>
            </div></body></html>
            """;
        using var client = new HttpClient(new FixtureHandler(html))
        {
            BaseAddress = new Uri("https://www.livescores.com")
        };
        var provider = new LiveScoreDailyResultsProvider(
            client,
            Options.Create(new LiveScoreOptions { Enabled = true, SourceTimeZoneId = "UTC" }));

        var result = await provider.FetchAsync(new DateOnly(2026, 7, 25), CancellationToken.None);

        Assert.Equal(2, result.Candidates.Count);
        var finished = result.Candidates.Single(x => x.SourceGameId == "1234567");
        Assert.Equal("finished", finished.Status);
        Assert.Equal((short)88, finished.HomeScore);
        Assert.Equal((short)76, finished.AwayScore);
        Assert.Equal("ACB", finished.CompetitionName);
        Assert.Equal("Play-off", finished.StageName);
        var scheduled = result.Candidates.Single(x => x.SourceGameId == "7654321");
        Assert.Equal("scheduled", scheduled.Status);
        Assert.Null(scheduled.HomeScore);
        Assert.Equal(new DateTime(2026, 7, 25, 20, 30, 0, DateTimeKind.Utc), scheduled.GameDateTimeUtc);
    }

    [Fact]
    public async Task FetchAsync_RejectsDisabledProvider()
    {
        using var client = new HttpClient(new FixtureHandler("<html />")) { BaseAddress = new Uri("https://www.livescores.com") };
        var provider = new LiveScoreDailyResultsProvider(client, Options.Create(new LiveScoreOptions { Enabled = false }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.FetchAsync(new DateOnly(2026, 7, 25), CancellationToken.None));
    }

    private sealed class FixtureHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        }
    }
}
