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
        var startedAtUtc = DateTime.UtcNow;
        int claimed;
        if (dbContext.Database.IsRelational())
        {
            claimed = await dbContext.EloRebuildRuns
                .Where(x => x.Id == runId && x.Status == EloRebuildRunStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, EloRebuildRunStatus.Running)
                    .SetProperty(x => x.StartedAtUtc, startedAtUtc), cancellationToken);
        }
        else
        {
            var pendingRun = await dbContext.EloRebuildRuns
                .SingleOrDefaultAsync(x => x.Id == runId && x.Status == EloRebuildRunStatus.Pending, cancellationToken);
            if (pendingRun is null)
            {
                claimed = 0;
            }
            else
            {
                pendingRun.Status = EloRebuildRunStatus.Running;
                pendingRun.StartedAtUtc = startedAtUtc;
                await dbContext.SaveChangesAsync(cancellationToken);
                claimed = 1;
            }
        }

        if (claimed == 0)
        {
            logger.LogInformation(
                "Skipping Hangfire ELO rebuild job for run {runId}; it is no longer pending.",
                runId);
            return;
        }

        logger.LogInformation("Processing system ELO rebuild run {runId}.", runId);
        try
        {
            var result = await rebuildService.RebuildAsync(runId, cancellationToken);
            if (result.Status == EloRebuildRunStatus.Failed)
            {
                throw new InvalidOperationException(
                    $"System ELO rebuild run '{runId}' failed: {result.Notes ?? "No failure details were recorded."}");
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
