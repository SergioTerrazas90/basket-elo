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
        var runId = await dbContext.EloRebuildRuns
            .Where(x => x.Status == EloRebuildRunStatus.Pending && x.HangfireJobId == null)
            .OrderBy(x => x.QueuedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (!runId.HasValue)
        {
            return false;
        }

        var hangfireJobId = jobDispatcher.EnqueueRebuild(runId.Value);
        int linked;
        if (dbContext.Database.IsRelational())
        {
            linked = await dbContext.EloRebuildRuns
                .Where(x => x.Id == runId.Value &&
                    x.Status == EloRebuildRunStatus.Pending &&
                    x.HangfireJobId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.HangfireJobId, hangfireJobId), cancellationToken);
        }
        else
        {
            var pendingRun = await dbContext.EloRebuildRuns
                .SingleOrDefaultAsync(x => x.Id == runId.Value &&
                    x.Status == EloRebuildRunStatus.Pending &&
                    x.HangfireJobId == null, cancellationToken);
            if (pendingRun is null)
            {
                linked = 0;
            }
            else
            {
                pendingRun.HangfireJobId = hangfireJobId;
                await dbContext.SaveChangesAsync(cancellationToken);
                linked = 1;
            }
        }

        if (linked == 0)
        {
            logger.LogInformation(
                "ELO rebuild run {runId} was already dispatched; duplicate Hangfire job {hangfireJobId} will safely no-op.",
                runId.Value,
                hangfireJobId);
            return true;
        }

        logger.LogInformation(
            "Dispatched ELO rebuild run {runId} as high-priority Hangfire job {hangfireJobId}.",
            runId.Value,
            hangfireJobId);
        return true;
    }
}
