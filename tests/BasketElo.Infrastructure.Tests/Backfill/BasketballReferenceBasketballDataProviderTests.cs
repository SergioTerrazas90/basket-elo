using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BasketballReferenceBasketballDataProviderTests
{
    [Theory]
    [InlineData("1946-1947", "BAA_1947")]
    [InlineData("1948-1949", "BAA_1949")]
    [InlineData("1949-1950", "NBA_1950")]
    [InlineData("2023-2024", "NBA_2024")]
    public void MapsCanonicalSeasonToSourceKey(string season, string expected)
    {
        Assert.Equal(expected, BasketballReferenceBasketballDataProvider.ToSourceSeasonKey(season));
    }

    [Theory]
    [InlineData("1946-1947", 3, "194611010TRH", "194704020PHW")]
    [InlineData("1949-1950", 3, "194910290TRI", "195003200WSC")]
    [InlineData("2022-2023", 4, "202210180BOS", null)]
    [InlineData("2023-2024", 3, "202310240DEN", "202404210BOS")]
    public async Task ParsesAuthorizedRegularAndPlayoffArchives(
        string season,
        int expectedCount,
        string expectedRegularGameId,
        string? expectedPlayoffGameId)
    {
        var provider = CreateProvider();
        var league = await provider.ResolveLeagueAsync(
            "United States",
            "NBA",
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        var first = await provider.GetGamesAsync(
            league!,
            season,
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);
        var second = await provider.GetGamesAsync(
            league!,
            season,
            new BackfillExecutionContext(0, 0),
            CancellationToken.None);

        Assert.Equal(expectedCount, first.Games.Count);
        Assert.Contains(first.Games, game => game.SourceGameId == expectedRegularGameId);
        if (expectedPlayoffGameId is not null)
        {
            Assert.Contains(first.Games, game => game.SourceGameId == expectedPlayoffGameId);
        }

        Assert.Equal(
            first.Games.Select(game => game.SourceGameId),
            second.Games.Select(game => game.SourceGameId));
        if (season == "1946-1947")
        {
            var inauguralGame = first.Games.Single(game => game.SourceGameId == "194611010TRH");
            Assert.Equal("Toronto Huskies", inauguralGame.HomeTeamName);
            Assert.Equal("TRH", inauguralGame.SourceHomeTeamId);
            Assert.Equal("New York Knicks", inauguralGame.AwayTeamName);
            Assert.Equal("NYK", inauguralGame.SourceAwayTeamId);
            Assert.Equal((short)66, inauguralGame.HomeScore);
            Assert.Equal((short)68, inauguralGame.AwayScore);
        }

        if (season == "2022-2023")
        {
            Assert.Contains(first.Games, game => game.Status == "postponed");
            Assert.Contains(first.Games, game => game.Status == "canceled");
            Assert.Contains(first.Warnings, warning => warning.Contains("only one final score", StringComparison.Ordinal));
        }

        if (season == "2023-2024")
        {
            Assert.Contains(first.Warnings, warning => warning.Contains("neutral-site", StringComparison.Ordinal));
        }

        Assert.All(first.Games, game =>
        {
            Assert.Equal(BasketballReferenceBasketballDataProvider.ParserVersion, game.Provenance?.ParserVersion);
            Assert.NotNull(game.Provenance?.SourceRevision);
        });
    }

    [Fact]
    public async Task ImportsThroughBackfillProcessorWithoutProviderSpecificLogic()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var provider = CreateProvider();
        var catalog = new BackfillCatalog();
        var processor = new BackfillJobProcessor(
            dbContext,
            [provider],
            new IdentityHealthCheckService(dbContext, catalog),
            catalog,
            NullLogger<BackfillJobProcessor>.Instance);
        dbContext.BackfillJobs.Add(new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = BasketballReferenceBasketballDataProvider.Source,
            Country = "United States",
            LeagueName = "NBA",
            Season = "1946-1947",
            DryRun = false,
            MaxRequests = 0
        });
        await dbContext.SaveChangesAsync();

        Assert.True(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));

        Assert.Equal(3, await dbContext.Games.CountAsync());
        Assert.Equal("NBA", (await dbContext.Competitions.SingleAsync()).Name);
        Assert.All(await dbContext.Games.ToListAsync(), game =>
        {
            Assert.Equal("BAA_1947", game.SourceSeasonKey);
            Assert.Equal(BasketballReferenceBasketballDataProvider.ParserVersion, game.ParserVersion);
        });
    }

    private static BasketballReferenceBasketballDataProvider CreateProvider()
    {
        var providerOptions = Options.Create(new BasketballReferenceOptions
        {
            ArchiveRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BasketballReference"),
            NetworkAccessEnabled = false
        });
        var httpClient = new HttpClient(new NoNetworkHandler())
        {
            BaseAddress = new Uri("https://www.basketball-reference.com/")
        };
        return new BasketballReferenceBasketballDataProvider(
            httpClient,
            providerOptions,
            new BasketballReferenceRateLimiter(providerOptions));
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Tests must use authorized local fixtures only.");
    }

}
