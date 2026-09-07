using System.Runtime;
using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 0)]
public sealed class SystemEloRebuildJob(
    BasketEloDbContext dbContext,
    IEloRebuildService rebuildService,
    ILogger<SystemEloRebuildJob> logger)
{
    [Queue(EloJobQueues.SystemElo)]
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        await ExecuteAsync([runId], cancellationToken);
    }

    [Queue(EloJobQueues.SystemElo)]
    public async Task ExecuteAsync(Guid[] runIds, CancellationToken cancellationToken)
    {
        if (runIds.Length == 0)
        {
            return;
        }

        var startedAtUtc = DateTime.UtcNow;
        var claimedRunIds = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .Where(x => runIds.Contains(x.Id) && x.Status == EloRebuildRunStatus.Pending)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        int claimed;
        if (dbContext.Database.IsRelational())
        {
            claimed = await dbContext.EloRebuildRuns
                .Where(x => runIds.Contains(x.Id) && x.Status == EloRebuildRunStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, EloRebuildRunStatus.Running)
                    .SetProperty(x => x.StartedAtUtc, startedAtUtc), cancellationToken);
        }
        else
        {
            var pendingRuns = await dbContext.EloRebuildRuns
                .Where(x => claimedRunIds.Contains(x.Id) && x.Status == EloRebuildRunStatus.Pending)
                .ToListAsync(cancellationToken);
            foreach (var pendingRun in pendingRuns)
            {
                pendingRun.Status = EloRebuildRunStatus.Running;
                pendingRun.StartedAtUtc = startedAtUtc;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            claimed = pendingRuns.Count;
        }

        if (claimed == 0)
        {
            logger.LogInformation(
                "Skipping Hangfire ELO rebuild job for runs {runIds}; none are still pending.",
                runIds);
            return;
        }

        if (claimed != claimedRunIds.Count)
        {
            claimedRunIds = await dbContext.EloRebuildRuns
                .AsNoTracking()
                .Where(x =>
                    claimedRunIds.Contains(x.Id) &&
                    x.Status == EloRebuildRunStatus.Running &&
                    x.StartedAtUtc == startedAtUtc)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        logger.LogInformation(
            "Processing {runCount} system ELO rebuild run(s) in one shared game stream.",
            claimedRunIds.Count);
        try
        {
            var results = await rebuildService.RebuildAsync(claimedRunIds, cancellationToken);
            var failed = results.FirstOrDefault(x => x.Status == EloRebuildRunStatus.Failed);
            if (failed is not null)
            {
                throw new InvalidOperationException(
                    $"System ELO rebuild run '{failed.RunId}' failed: {failed.Notes ?? "No failure details were recorded."}");
            }
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
    }
}
