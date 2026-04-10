namespace BasketElo.Infrastructure.Backfill;

public interface IBackfillJobProcessor
{
    Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken);
}
