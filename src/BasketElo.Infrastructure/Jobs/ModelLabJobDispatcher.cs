using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace BasketElo.Infrastructure.Jobs;

public interface IModelLabJobDispatcher
{
    string EnqueueRun(Guid runId);
    bool Delete(string jobId);
}

public sealed class ModelLabJobDispatcher(IBackgroundJobClient backgroundJobs) : IModelLabJobDispatcher
{
    public string EnqueueRun(Guid runId) => backgroundJobs.Create(
        Job.FromExpression<ModelLabRunJob>(job =>
            job.ExecuteAsync(runId, CancellationToken.None)),
        new EnqueuedState(EloJobQueues.ModelLab));

    public bool Delete(string jobId) => backgroundJobs.Delete(jobId);
}
