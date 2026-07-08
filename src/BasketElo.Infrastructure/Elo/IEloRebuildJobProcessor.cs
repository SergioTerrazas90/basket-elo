namespace BasketElo.Infrastructure.Elo;

public interface IEloRebuildJobProcessor
{
    Task<bool> TryProcessNextPendingJobAsync(CancellationToken cancellationToken);
}
