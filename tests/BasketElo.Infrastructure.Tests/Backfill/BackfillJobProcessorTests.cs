using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class BackfillJobProcessorTests
{
    [Fact]
    public async Task ReimportUpdatesGameAndProvenanceWithoutCreatingDuplicate()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var provider = new TestProvider();
        var catalog = new TestCatalog();
        var identityService = new IdentityHealthCheckService(dbContext, catalog);
        var processor = new BackfillJobProcessor(
            dbContext,
            [provider],
            identityService,
            catalog,
            NullLogger<BackfillJobProcessor>.Instance);

        var firstJob = CreateJob();
        dbContext.BackfillJobs.Add(firstJob);
        await dbContext.SaveChangesAsync();

        Assert.True(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));

        var firstImport = await dbContext.Games.SingleAsync();
        var ingestedAtUtc = firstImport.IngestedAtUtc;
        Assert.Equal((short)90, firstImport.HomeScore);
        Assert.Equal("https://data.example.test/seasons/2024", firstImport.SourceUrl);
        Assert.Equal("2024", firstImport.SourceSeasonKey);
        Assert.Equal("fixture-parser-v1", firstImport.ParserVersion);

        provider.HomeScore = 94;
        provider.Provenance = new BasketballProviderGameProvenance(
            "https://data.example.test/seasons/2024-revised",
            "2024",
            new DateTime(2025, 1, 3, 12, 0, 0, DateTimeKind.Utc),
            "fixture-parser-v2",
            "revision-2");

        var secondJob = CreateJob();
        dbContext.BackfillJobs.Add(secondJob);
        await dbContext.SaveChangesAsync();

        Assert.True(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));

        var updated = await dbContext.Games.SingleAsync();
        Assert.Equal(ingestedAtUtc, updated.IngestedAtUtc);
        Assert.Equal((short)94, updated.HomeScore);
        Assert.Equal("https://data.example.test/seasons/2024-revised", updated.SourceUrl);
        Assert.Equal("revision-2", updated.SourceRevision);
        Assert.Equal("fixture-parser-v2", updated.ParserVersion);

        using var summary = JsonDocument.Parse(secondJob.SummaryJson!);
        Assert.Equal(0, summary.RootElement.GetProperty("GamesInserted").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("GamesUpdated").GetInt32());
        Assert.Equal(
            "fixture-parser-v2",
            summary.RootElement.GetProperty("ParserVersions")[0].GetString());
    }

    private static BackfillJob CreateJob() => new()
    {
        Id = Guid.NewGuid(),
        Provider = TestProvider.Source,
        Country = "Spain",
        LeagueName = "ACB",
        Season = "2024-2025",
        DryRun = false,
        MaxRequests = 2
    };

    private sealed class TestProvider : IBasketballDataProvider
    {
        public const string Source = "test-source";

        public string SourceKey => Source;
        public short HomeScore { get; set; } = 90;
        public BasketballProviderGameProvenance Provenance { get; set; } = new(
            "https://data.example.test/seasons/2024",
            "2024",
            new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc),
            "fixture-parser-v1");

        public Task<BasketballProviderLeague?> ResolveLeagueAsync(
            string country,
            string leagueName,
            BackfillExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.ConsumeRequest();
            return Task.FromResult<BasketballProviderLeague?>(
                new BasketballProviderLeague(Source, "acb", "ACB", "ES"));
        }

        public Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
            BasketballProviderLeague league,
            string season,
            BackfillExecutionContext context,
            CancellationToken cancellationToken)
        {
            context.ConsumeRequest();
            IReadOnlyCollection<BasketballProviderGame> games =
            [
                new(
                    Source,
                    "game-1",
                    new DateTime(2025, 1, 1, 20, 0, 0, DateTimeKind.Utc),
                    "finished",
                    "home-1",
                    "Home Club",
                    "away-1",
                    "Away Club",
                    HomeScore,
                    88,
                    Provenance)
            ];
            return Task.FromResult((games, false, (IReadOnlyCollection<string>)[]));
        }
    }

    private sealed class TestCatalog : IBackfillCatalog
    {
        private static readonly ConfiguredBackfillLeague League = new(
            TestProvider.Source,
            "Spain",
            "ACB",
            "Spain: ACB",
            "2024-2025");

        public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => [League];

        public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league) =>
            ["2024-2025"];
    }
}
