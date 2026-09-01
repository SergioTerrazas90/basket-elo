using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace BasketElo.Infrastructure.Jobs;

public interface ISystemEloJobDispatcher
{
    string EnqueueRebuild(Guid runId);
}

public sealed class SystemEloJobDispatcher(IBackgroundJobClient backgroundJobs) : ISystemEloJobDispatcher
{
    public string EnqueueRebuild(Guid runId) => backgroundJobs.Create(
        Job.FromExpression<SystemEloRebuildJob>(job =>
            job.ExecuteAsync(runId, CancellationToken.None)),
        new EnqueuedState(EloJobQueues.SystemElo));
}
