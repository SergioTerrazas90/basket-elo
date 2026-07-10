namespace BasketElo.Infrastructure.Backfill;

public interface IApiSportsRateLimiter
{
    Task WaitAsync(CancellationToken cancellationToken);
}
