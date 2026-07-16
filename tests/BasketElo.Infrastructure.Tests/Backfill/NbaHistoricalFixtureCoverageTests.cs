using BasketElo.Domain.Backfill;
using BasketElo.Infrastructure.Backfill;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaHistoricalFixtureCoverageTests
{
    public static TheoryData<string, int, DateTime, DateTime, string, string> SeasonExpectations => new()
    {
        { "1946-1947", 3, Utc(1946, 11, 1), Utc(1947, 4, 2), "Toronto Huskies", "194704020PHW" },
        { "1949-1950", 3, Utc(1949, 10, 29), Utc(1950, 3, 20), "Tri-Cities Blackhawks", "195003200WSC" },
        { "1965-1966", 3, Utc(1965, 10, 15), Utc(1966, 4, 28), "San Francisco Warriors", "196604280BOS" },
        { "1998-1999", 3, Utc(1999, 2, 5), Utc(1999, 6, 25), "San Antonio Spurs", "199906250NYK" },
        { "2011-2012", 3, Utc(2011, 12, 25), Utc(2012, 6, 21), "Oklahoma City Thunder", "201206210MIA" },
        { "2019-2020", 3, Utc(2019, 10, 22), Utc(2020, 10, 11), "Los Angeles Lakers", "202010110LAL" },
        { "2025-2026", 4, Utc(2025, 10, 21), Utc(2026, 6, 13), "New York Knicks", "202606130SAS" }
    };

    [Theory]
    [MemberData(nameof(SeasonExpectations))]
    public async Task FixtureHasExpectedBoundariesTeamsAndPlayoffs(
        string season,
        int expectedCount,
        DateTime expectedFirstDate,
        DateTime expectedLastDate,
        string representativeTeam,
        string playoffGameId)
    {
        var providerOptions = Options.Create(new BasketballReferenceOptions
        {
            ArchiveRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BasketballReference")
        });
        var provider = new BasketballReferenceBasketballDataProvider(
            new HttpClient(new NoNetworkHandler())
            {
                BaseAddress = new Uri("https://www.basketball-reference.com/")
            },
            providerOptions,
            new BasketballReferenceRateLimiter(providerOptions));
        var league = await provider.ResolveLeagueAsync(
            "United States",
            "NBA",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        var result = await provider.GetGamesAsync(
            league!,
            season,
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var ordered = result.Games.OrderBy(game => game.GameDateTimeUtc).ToList();

        Assert.Equal(expectedCount, ordered.Count);
        Assert.Equal(expectedFirstDate, ordered[0].GameDateTimeUtc);
        Assert.Equal(expectedLastDate, ordered[^1].GameDateTimeUtc);
        Assert.Contains(ordered, game =>
            game.HomeTeamName == representativeTeam || game.AwayTeamName == representativeTeam);
        Assert.Contains(ordered, game => game.SourceGameId == playoffGameId);
        if (season is "1998-1999" or "2011-2012")
        {
            Assert.Empty(result.Warnings);
        }

        Assert.DoesNotContain(result.Warnings, warning =>
            warning.Contains("No schedule rows", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("only the first page", StringComparison.OrdinalIgnoreCase));
    }

    private static DateTime Utc(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fixture coverage tests must not use the network.");
    }
}
