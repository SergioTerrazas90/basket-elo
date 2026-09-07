using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace BasketElo.Infrastructure.Jobs;

public interface ISystemEloJobDispatcher
{
    string EnqueueRebuild(Guid[] runIds);
}

public sealed class SystemEloJobDispatcher(IBackgroundJobClient backgroundJobs) : ISystemEloJobDispatcher
{
    public string EnqueueRebuild(Guid[] runIds) => backgroundJobs.Create(
        Job.FromExpression<SystemEloRebuildJob>(job =>
            job.ExecuteAsync(runIds, CancellationToken.None)),
        new EnqueuedState(EloJobQueues.SystemElo));
}
