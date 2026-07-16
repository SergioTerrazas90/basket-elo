using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloRebuildJobProcessorTests
{
    [Fact]
    public async Task CompletedRebuildClearsTrackedEntities()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var run = new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            EloPoolKey = EloPoolKeys.Nba,
            RulesetVersion = EloRulesetVersions.AdjustedV1,
            CompetitionName = string.Empty,
            Status = EloRebuildRunStatus.Pending,
            QueuedAtUtc = DateTime.UtcNow
        };
        dbContext.EloRebuildRuns.Add(run);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = new TrackingRebuildService(dbContext);
        var processor = new EloRebuildJobProcessor(
            dbContext,
            service,
            NullLogger<EloRebuildJobProcessor>.Instance);

        var processed = await processor.TryProcessNextPendingJobAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Empty(dbContext.ChangeTracker.Entries());
    }

    private sealed class TrackingRebuildService(BasketEloDbContext dbContext) : IEloRebuildService
    {
        public Task<EloRebuildResult> RebuildAsync(Guid runId, CancellationToken cancellationToken)
        {
            dbContext.Teams.Add(new Team
            {
                Id = Guid.NewGuid(),
                CanonicalName = "Tracked during rebuild",
                CountryCode = "USA"
            });

            return Task.FromResult(new EloRebuildResult
            {
                RunId = runId,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Status = EloRebuildRunStatus.Completed
            });
        }
    }
}
