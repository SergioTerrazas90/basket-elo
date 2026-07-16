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

public class NbaFranchiseImportTests
{
    [Fact]
    public async Task RelocationsReuseTeamsAndDefunctClubsDoNotBlockIdentityHealth()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var catalog = new BackfillCatalog();
        var provider = new FranchiseFixtureProvider();
        var processor = new BackfillJobProcessor(
            dbContext,
            [provider],
            new IdentityHealthCheckService(dbContext, catalog),
            catalog,
            NullLogger<BackfillJobProcessor>.Instance);

        var jobs = new[]
        {
            CreateJob("1959-1960"),
            CreateJob("1960-1961"),
            CreateJob("1946-1947")
        };
        foreach (var job in jobs)
        {
            dbContext.BackfillJobs.Add(job);
            await dbContext.SaveChangesAsync();
            Assert.True(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));
        }

        var lakers = await dbContext.Teams.SingleAsync(team => team.CanonicalName == "Los Angeles Lakers");
        var lakerAliases = await dbContext.TeamAliases
            .Where(alias => alias.TeamId == lakers.Id)
            .OrderBy(alias => alias.SourceTeamId)
            .ToListAsync();
        Assert.Equal(["LAL", "MNL"], lakerAliases.Select(alias => alias.SourceTeamId));
        Assert.All(lakerAliases, alias => Assert.NotNull(alias.ValidFromUtc));

        var pistons = await dbContext.Teams.SingleAsync(team => team.CanonicalName == "Detroit Pistons");
        Assert.Equal(
            ["DET", "FTW"],
            await dbContext.TeamAliases
                .Where(alias => alias.TeamId == pistons.Id)
                .OrderBy(alias => alias.SourceTeamId)
                .Select(alias => alias.SourceTeamId)
                .ToListAsync());

        Assert.False((await dbContext.Teams.SingleAsync(team => team.CanonicalName == "Detroit Falcons")).IsActive);
        Assert.False((await dbContext.Teams.SingleAsync(team => team.CanonicalName == "Toronto Huskies")).IsActive);
        using var summary = JsonDocument.Parse(jobs[^1].SummaryJson!);
        Assert.Equal(0, summary.RootElement.GetProperty("IdentityBlockersCount").GetInt32());
    }

    private static BackfillJob CreateJob(string season) => new()
    {
        Id = Guid.NewGuid(),
        Provider = BasketballReferenceBasketballDataProvider.Source,
        Country = "United States",
        LeagueName = "NBA",
        Season = season,
        DryRun = false,
        MaxRequests = 0
    };

    private sealed class FranchiseFixtureProvider : IBasketballDataProvider
    {
        public string SourceKey => BasketballReferenceBasketballDataProvider.Source;

        public Task<BasketballProviderLeague?> ResolveLeagueAsync(
            string country,
            string leagueName,
            BackfillExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult<BasketballProviderLeague?>(new(
                SourceKey,
                "NBA",
                "NBA",
                "USA"));

        public Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
            BasketballProviderLeague league,
            string season,
            BackfillExecutionContext context,
            CancellationToken cancellationToken)
        {
            var games = season switch
            {
                "1959-1960" => Games("MNL", "Minneapolis Lakers", "FTW", "Fort Wayne Pistons", season),
                "1960-1961" => Games("LAL", "Los Angeles Lakers", "DET", "Detroit Pistons", season),
                "1946-1947" => Games("DTF", "Detroit Falcons", "TRH", "Toronto Huskies", season),
                _ => throw new InvalidOperationException($"Unexpected fixture season '{season}'.")
            };
            return Task.FromResult((games, false, (IReadOnlyCollection<string>)[]));
        }

        private static IReadOnlyCollection<BasketballProviderGame> Games(
            string homeId,
            string homeName,
            string awayId,
            string awayName,
            string season) =>
            [
                new(
                    BasketballReferenceBasketballDataProvider.Source,
                    $"{season}-{homeId}-{awayId}",
                    new DateTime(int.Parse(season[..4]), 11, 1, 12, 0, 0, DateTimeKind.Utc),
                    "finished",
                    homeId,
                    homeName,
                    awayId,
                    awayName,
                    100,
                    90)
            ];
    }
}
