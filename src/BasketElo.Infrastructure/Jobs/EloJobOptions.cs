namespace BasketElo.Infrastructure.Jobs;

public sealed class EloJobOptions
{
    public const string SectionName = "EloJobs";

    public int WorkerCount { get; set; } = 3;

    public int EffectiveWorkerCount => Math.Clamp(WorkerCount, 1, 3);
}

public static class EloJobQueues
{
    // Hangfire.PostgreSql processes queues alphabetically.
    public const string SystemElo = "a-system-elo";
    public const string ModelLab = "z-model-lab";

    public static readonly string[] InPriorityOrder = [SystemElo, ModelLab];
}
