using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Jobs;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloRebuildJobProcessorTests
{
    [Fact]
    public async Task PendingRebuildIsLinkedToHighPriorityHangfireJob()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var run = CreateRun();
        dbContext.EloRebuildRuns.Add(run);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var dispatcher = new RecordingDispatcher();
        var processor = new EloRebuildJobProcessor(
            dbContext,
            dispatcher,
            NullLogger<EloRebuildJobProcessor>.Instance);

        var processed = await processor.TryProcessNextPendingJobAsync(CancellationToken.None);

        Assert.True(processed);
        Assert.Equal(run.Id, Assert.Single(dispatcher.RunIds));
        var stored = await dbContext.EloRebuildRuns.SingleAsync();
        Assert.Equal("hangfire-job-1", stored.HangfireJobId);
        Assert.Equal(EloRebuildRunStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task AlreadyDispatchedRebuildIsNotQueuedAgain()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var run = CreateRun();
        run.HangfireJobId = "existing-job";
        dbContext.EloRebuildRuns.Add(run);
        await dbContext.SaveChangesAsync();
        var dispatcher = new RecordingDispatcher();
        var processor = new EloRebuildJobProcessor(
            dbContext,
            dispatcher,
            NullLogger<EloRebuildJobProcessor>.Instance);

        var processed = await processor.TryProcessNextPendingJobAsync(CancellationToken.None);

        Assert.False(processed);
        Assert.Empty(dispatcher.RunIds);
    }

    [Fact]
    public async Task HangfireJobAtomicallyClaimsAndCompletesRebuild()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var run = CreateRun();
        run.HangfireJobId = "hangfire-job-1";
        dbContext.EloRebuildRuns.Add(run);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        var service = new CompletingRebuildService(dbContext);
        var job = new SystemEloRebuildJob(
            dbContext,
            service,
            NullLogger<SystemEloRebuildJob>.Instance);

        await job.ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Equal(run.Id, Assert.Single(service.RunIds));
        Assert.Empty(dbContext.ChangeTracker.Entries());
        await using var verificationContext = new BasketEloDbContext(options);
        var stored = await verificationContext.EloRebuildRuns.SingleAsync();
        Assert.Equal(EloRebuildRunStatus.Completed, stored.Status);
        Assert.NotNull(stored.StartedAtUtc);
    }

    [Fact]
    public async Task DuplicateHangfireDeliveryDoesNotRunCompletedRebuildAgain()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var run = CreateRun();
        run.Status = EloRebuildRunStatus.Completed;
        run.HangfireJobId = "hangfire-job-1";
        dbContext.EloRebuildRuns.Add(run);
        await dbContext.SaveChangesAsync();
        var service = new CompletingRebuildService(dbContext);
        var job = new SystemEloRebuildJob(
            dbContext,
            service,
            NullLogger<SystemEloRebuildJob>.Instance);

        await job.ExecuteAsync(run.Id, CancellationToken.None);

        Assert.Empty(service.RunIds);
    }

    private static EloRebuildRun CreateRun() => new()
    {
        Id = Guid.NewGuid(),
        EloPoolKey = EloPoolKeys.Nba,
        RulesetVersion = EloRulesetVersions.AdjustedV1,
        CompetitionName = string.Empty,
        Status = EloRebuildRunStatus.Pending,
        QueuedAtUtc = DateTime.UtcNow
    };

    private sealed class RecordingDispatcher : ISystemEloJobDispatcher
    {
        public List<Guid> RunIds { get; } = [];

        public string EnqueueRebuild(Guid runId)
        {
            RunIds.Add(runId);
            return $"hangfire-job-{RunIds.Count}";
        }
    }

    private sealed class CompletingRebuildService(BasketEloDbContext dbContext) : IEloRebuildService
    {
        public List<Guid> RunIds { get; } = [];

        public async Task<EloRebuildResult> RebuildAsync(Guid runId, CancellationToken cancellationToken)
        {
            RunIds.Add(runId);
            var run = await dbContext.EloRebuildRuns.SingleAsync(x => x.Id == runId, cancellationToken);
            Assert.Equal(EloRebuildRunStatus.Running, run.Status);
            run.Status = EloRebuildRunStatus.Completed;
            run.FinishedAtUtc = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            return new EloRebuildResult
            {
                RunId = runId,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Status = EloRebuildRunStatus.Completed
            };
        }
    }
}
