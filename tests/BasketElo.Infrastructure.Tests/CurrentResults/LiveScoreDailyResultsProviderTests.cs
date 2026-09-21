using System.Net;
using System.Net.Http.Headers;
using BasketElo.Domain.CurrentResults;
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
    public async Task FetchAsync_UsesParentCompetitionForGroupedHeaders()
    {
        const string html = """
            <html><body><div class="group">
              <div class="Pa"><span><a href="/basketball/asia-u18-championship/"><span class="Sa">Asia U18 Championship</span></a> - <a href="/basketball/asia-u18-championship/group-a/"><span class="Ta">Group A</span></a></span><span class="Qa">August 13</span></div>
              <div class="Xe"><button data-eventId="1854829"></button><span class="Ih">05:00</span><div class="nf"><div class="vf">Australia U18</div><div class="vf">Thailand U18</div></div><div class="rf"></div></div>
            </div></body></html>
            """;
        using var client = new HttpClient(new FixtureHandler(html))
        {
            BaseAddress = new Uri("https://www.livescores.com")
        };
        var provider = new LiveScoreDailyResultsProvider(
            client,
            Options.Create(new LiveScoreOptions { Enabled = true, SourceTimeZoneId = "UTC" }));

        var result = await provider.FetchAsync(new DateOnly(2026, 8, 13), CancellationToken.None);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Asia U18 Championship", candidate.CompetitionName);
        Assert.Equal("Group A", candidate.StageName);
        Assert.Equal("Asia U18 Championship", candidate.CountryName);
    }

    [Fact]
    public async Task FetchAsync_UsesEmbeddedEventTimestampAsUtc()
    {
        var epoch = new DateTimeOffset(2026, 8, 27, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var html = $"""
            <html><body><div class="group">
              <div class="Pa"><span class="Sa">World</span><span class="Ta">Qualification</span></div>
              <div class="Xe"><button data-eventId="1819479" data-favouritesDetails="basketball-1819479-{epoch}"></button><span class="Ih">18:00</span><div class="nf"><div class="vf">Lebanon</div><div class="vf">South Korea</div></div><div class="rf"></div></div>
            </div></body></html>
            """;
        using var client = new HttpClient(new FixtureHandler(html))
        {
            BaseAddress = new Uri("https://www.livescores.com")
        };
        var provider = new LiveScoreDailyResultsProvider(
            client,
            Options.Create(new LiveScoreOptions { Enabled = true, SourceTimeZoneId = "Europe/Madrid" }));

        var result = await provider.FetchAsync(new DateOnly(2026, 8, 27), CancellationToken.None);

        var game = Assert.Single(result.Candidates);
        Assert.Equal("1819479", game.SourceGameId);
        Assert.Equal(new DateTime(2026, 8, 27, 18, 0, 0, DateTimeKind.Utc), game.GameDateTimeUtc);
    }

    [Fact]
    public async Task FetchAsync_ParsesCurrentFinishedAndScheduledMarkup()
    {
        var epoch = new DateTimeOffset(2026, 9, 14, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var html = $"""
            <html><body><div class="group">
              <div class="Ea"><span><a href="/basketball/great-britain/"><span class="Ha">Great Britain</span></a> - <a href="/basketball/great-britain/slb/"><span class="Ia">SLB</span></a></span></div>
              <div class="rf wf"><a href="/basketball/great-britain/slb/bristol-flyers-vs-london-lions/1890495/"><div class="Cf"><span class="ug wg vg"><span class="zg vg">FT</span></span><div class="Df"><div class="Hf"><div class="Pf">Bristol Flyers</div><div class="Pf">London Lions</div></div><div class="Lf Mf Nf"><span class="Bf">86</span><span class="Bf">93</span></div></div></div></a></div>
              <div class="Ea"><span><a href="/basketball/champions-league/"><span class="Ha">Champions League</span></a> - <a href="/basketball/champions-league/qualification/"><span class="Ia">Qualification</span></a></span></div>
              <div class="rf"><div class="Cf"><span class="ug wg vg"><span class="zg vg">18:00</span></span><div class="Df"><div class="Hf"><div class="Pf">Wuerzburg Baskets</div><div class="Pf">Manchester Basketball</div></div><div class="Lf Mf"><span class="Bf"></span><span class="Bf"></span></div></div></div><button data-eventId="1820576" data-favouritesDetails="basketball-1820576-{epoch}"></button></div>
            </div></body></html>
            """;
        using var client = new HttpClient(new FixtureHandler(html))
        {
            BaseAddress = new Uri("https://www.livescores.com")
        };
        var provider = new LiveScoreDailyResultsProvider(
            client,
            Options.Create(new LiveScoreOptions { Enabled = true, SourceTimeZoneId = "UTC" }));

        var result = await provider.FetchAsync(new DateOnly(2026, 9, 14), CancellationToken.None);

        Assert.Equal(2, result.Candidates.Count);
        var finished = result.Candidates.Single(x => x.SourceGameId == "1890495");
        Assert.Equal("Great Britain", finished.CountryName);
        Assert.Equal("SLB", finished.CompetitionName);
        Assert.Null(finished.StageName);
        Assert.Equal(CurrentResultStatuses.Finished, finished.Status);
        Assert.Equal((short)86, finished.HomeScore);
        Assert.Equal((short)93, finished.AwayScore);

        var scheduled = result.Candidates.Single(x => x.SourceGameId == "1820576");
        Assert.Equal("Champions League", scheduled.CountryName);
        Assert.Equal("Champions League", scheduled.CompetitionName);
        Assert.Equal("Qualification", scheduled.StageName);
        Assert.Equal(CurrentResultStatuses.Scheduled, scheduled.Status);
        Assert.Equal(new DateTime(2026, 9, 14, 18, 0, 0, DateTimeKind.Utc), scheduled.GameDateTimeUtc);
    }

    [Fact]
    public async Task FetchAsync_RejectsUnrecognizedMarkupWhenEventsArePresent()
    {
        const string html = """
            <html><body><div class="new-layout"><button data-eventId="1234567"></button><span>Team A</span><span>Team B</span></div></body></html>
            """;
        using var client = new HttpClient(new FixtureHandler(html))
        {
            BaseAddress = new Uri("https://www.livescores.com")
        };
        var provider = new LiveScoreDailyResultsProvider(
            client,
            Options.Create(new LiveScoreOptions { Enabled = true, SourceTimeZoneId = "UTC" }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.FetchAsync(new DateOnly(2026, 9, 14), CancellationToken.None));

        Assert.Contains("parser likely needs updating", exception.Message, StringComparison.OrdinalIgnoreCase);
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
