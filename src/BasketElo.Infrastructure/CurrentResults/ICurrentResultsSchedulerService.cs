namespace BasketElo.Infrastructure.CurrentResults;

public interface ICurrentResultsSchedulerService
{
    Task<(bool Queued, DateOnly FromDate, DateOnly ToDate, string? Status)> QueueIfDueAsync(CancellationToken cancellationToken);
}
