using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Jobs;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Elo;

public sealed class EloRebuildJobProcessor(
    BasketEloDbContext dbContext,
    ISystemEloJobDispatcher jobDispatcher,
    ILogger<EloRebuildJobProcessor> logger) : IEloRebuildJobProcessor
{
    public async Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken)
    {
        var nextRun = await dbContext.EloRebuildRuns
            .Where(x => x.Status == EloRebuildRunStatus.Pending && x.HangfireJobId == null)
            .OrderBy(x => x.QueuedAtUtc)
            .Select(x => new { x.Id, x.EloPoolKey })
            .FirstOrDefaultAsync(cancellationToken);

        if (nextRun is null)
        {
            return false;
        }

        var candidates = await dbContext.EloRebuildRuns
            .AsNoTracking()
            .Where(x =>
                x.EloPoolKey == nextRun.EloPoolKey &&
                x.Status == EloRebuildRunStatus.Pending &&
                x.HangfireJobId == null)
            .OrderBy(x => x.QueuedAtUtc)
            .Select(x => new { x.Id, x.RulesetVersion })
            .ToListAsync(cancellationToken);
        var runIds = candidates
            .GroupBy(x => x.RulesetVersion, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First().Id)
            .ToArray();

        var hangfireJobId = jobDispatcher.EnqueueRebuild(runIds);
        int linked;
        if (dbContext.Database.IsRelational())
        {
            linked = await dbContext.EloRebuildRuns
                .Where(x => runIds.Contains(x.Id) &&
                    x.Status == EloRebuildRunStatus.Pending &&
                    x.HangfireJobId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.HangfireJobId, hangfireJobId), cancellationToken);
        }
        else
        {
            var pendingRun = await dbContext.EloRebuildRuns
                .Where(x => runIds.Contains(x.Id) &&
                    x.Status == EloRebuildRunStatus.Pending &&
                    x.HangfireJobId == null)
                .ToListAsync(cancellationToken);
            foreach (var run in pendingRun)
            {
                run.HangfireJobId = hangfireJobId;
            }
            await dbContext.SaveChangesAsync(cancellationToken);
            linked = pendingRun.Count;
        }

        if (linked == 0)
        {
            logger.LogInformation(
                "ELO rebuild runs {runIds} were already dispatched; duplicate Hangfire job {hangfireJobId} will safely no-op.",
                runIds,
                hangfireJobId);
            return true;
        }

        logger.LogInformation(
            "Dispatched {runCount} ELO rebuild run(s) for pool {poolKey} as high-priority Hangfire job {hangfireJobId}.",
            linked,
            nextRun.EloPoolKey,
            hangfireJobId);
        return true;
    }
}
