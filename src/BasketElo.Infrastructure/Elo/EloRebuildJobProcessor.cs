using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Runtime;

namespace BasketElo.Infrastructure.Elo;

public sealed class EloRebuildJobProcessor(
    BasketEloDbContext dbContext,
    IEloRebuildService rebuildService,
    ILogger<EloRebuildJobProcessor> logger) : IEloRebuildJobProcessor
{
    public async Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var runId = await dbContext.EloRebuildRuns
            .Where(x => x.Status == EloRebuildRunStatus.Pending)
            .OrderBy(x => x.QueuedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!runId.HasValue)
        {
            return false;
        }

        var startedAtUtc = DateTime.UtcNow;
        int claimed;
        if (dbContext.Database.IsRelational())
        {
            claimed = await dbContext.EloRebuildRuns
                .Where(x => x.Id == runId.Value && x.Status == EloRebuildRunStatus.Pending)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, EloRebuildRunStatus.Running)
                    .SetProperty(x => x.StartedAtUtc, startedAtUtc), cancellationToken);
        }
        else
        {
            var pendingRun = await dbContext.EloRebuildRuns
                .SingleOrDefaultAsync(x => x.Id == runId.Value && x.Status == EloRebuildRunStatus.Pending, cancellationToken);
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
            return true;
        }

        logger.LogInformation("Processing ELO rebuild run {runId}.", runId.Value);
        try
        {
            await rebuildService.RebuildAsync(runId.Value, cancellationToken);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
        return true;
    }
}
