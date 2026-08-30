using System.Runtime;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Jobs;

[AutomaticRetry(Attempts = 0)]
public sealed class ModelLabRunJob(
    BasketEloDbContext dbContext,
    IModelLabRunService runService,
    ILogger<ModelLabRunJob> logger)
{
    [Queue(EloJobQueues.ModelLab)]
    public async Task ExecuteAsync(Guid runId, CancellationToken cancellationToken)
    {
        if (!await TryClaimAsync(runId, cancellationToken))
        {
            logger.LogInformation(
                "Skipping Hangfire Model Lab job for run {runId}; it is no longer queued.",
                runId);
            return;
        }

        logger.LogInformation("Processing Model Lab run {runId}.", runId);
        try
        {
            await runService.ExecuteAsync(runId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SetTerminalStateAsync(
                runId,
                ModelLabRunStatuses.Queued,
                0,
                "Waiting for a worker",
                "Worker stopped during the run; it was returned to the queue.",
                clearJobId: true);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Model Lab run {runId} failed.", runId);
            await SetTerminalStateAsync(
                runId,
                ModelLabRunStatuses.Failed,
                100,
                "Failed",
                exception.Message,
                clearJobId: false);
            throw;
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }
    }

    private async Task<bool> TryClaimAsync(Guid runId, CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        if (dbContext.Database.IsRelational())
        {
            var claimed = await dbContext.ModelLabRuns
                .Where(x => x.Id == runId && x.Status == ModelLabRunStatuses.Queued)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, ModelLabRunStatuses.Running)
                    .SetProperty(x => x.StartedAtUtc, startedAtUtc)
                    .SetProperty(x => x.ProgressPercent, 10)
                    .SetProperty(x => x.ProgressStage, "Calculating ratings"), cancellationToken);
            return claimed == 1;
        }

        var run = await dbContext.ModelLabRuns
            .SingleOrDefaultAsync(x => x.Id == runId && x.Status == ModelLabRunStatuses.Queued, cancellationToken);
        if (run is null)
        {
            return false;
        }

        run.Status = ModelLabRunStatuses.Running;
        run.StartedAtUtc = startedAtUtc;
        run.ProgressPercent = 10;
        run.ProgressStage = "Calculating ratings";
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task SetTerminalStateAsync(
        Guid runId,
        string status,
        int progressPercent,
        string progressStage,
        string? errorMessage,
        bool clearJobId)
    {
        dbContext.ChangeTracker.Clear();
        var run = await dbContext.ModelLabRuns.SingleAsync(x => x.Id == runId, CancellationToken.None);
        run.Status = status;
        run.ProgressPercent = progressPercent;
        run.ProgressStage = progressStage;
        run.ErrorMessage = errorMessage;
        run.CompletedAtUtc = status == ModelLabRunStatuses.Queued ? null : DateTime.UtcNow;
        if (status == ModelLabRunStatuses.Queued)
        {
            run.StartedAtUtc = null;
        }
        if (clearJobId)
        {
            run.HangfireJobId = null;
        }
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }
}
