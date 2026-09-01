using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Jobs;

public interface IModelLabRunJobProcessor
{
    Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken);
}

public sealed class ModelLabRunJobProcessor(
    BasketEloDbContext dbContext,
    IModelLabJobDispatcher jobDispatcher,
    ILogger<ModelLabRunJobProcessor> logger) : IModelLabRunJobProcessor
{
    public async Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken)
    {
        // Model Lab backtests share the historical ELO pipeline. Keep one job in flight
        // at a time so comparison batches are orchestrated instead of competing for it.
        var hasInFlightJob = await dbContext.ModelLabRuns
            .AsNoTracking()
            .AnyAsync(x =>
                x.Status == ModelLabRunStatuses.Running ||
                (x.Status == ModelLabRunStatuses.Queued && x.HangfireJobId != null),
                cancellationToken);
        if (hasInFlightJob)
        {
            return false;
        }

        var runId = await dbContext.ModelLabRuns
            .Where(x => x.Status == ModelLabRunStatuses.Queued && x.HangfireJobId == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!runId.HasValue)
        {
            return false;
        }

        var hangfireJobId = jobDispatcher.EnqueueRun(runId.Value);
        int linked;
        if (dbContext.Database.IsRelational())
        {
            linked = await dbContext.ModelLabRuns
                .Where(x => x.Id == runId.Value &&
                    x.Status == ModelLabRunStatuses.Queued &&
                    x.HangfireJobId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.HangfireJobId, hangfireJobId), cancellationToken);
        }
        else
        {
            var run = await dbContext.ModelLabRuns.SingleOrDefaultAsync(x =>
                x.Id == runId.Value &&
                x.Status == ModelLabRunStatuses.Queued &&
                x.HangfireJobId == null,
                cancellationToken);
            if (run is null)
            {
                linked = 0;
            }
            else
            {
                run.HangfireJobId = hangfireJobId;
                await dbContext.SaveChangesAsync(cancellationToken);
                linked = 1;
            }
        }

        logger.LogInformation(
            linked == 1
                ? "Dispatched Model Lab run {runId} as Hangfire job {hangfireJobId}."
                : "Model Lab run {runId} was already dispatched; duplicate Hangfire job {hangfireJobId} will safely no-op.",
            runId.Value,
            hangfireJobId);
        return true;
    }
}
