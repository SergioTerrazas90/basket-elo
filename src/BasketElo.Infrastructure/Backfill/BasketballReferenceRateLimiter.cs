using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public interface IBasketballReferenceRateLimiter
{
    Task WaitAsync(CancellationToken cancellationToken);
}

public sealed class BasketballReferenceRateLimiter(
    IOptions<BasketballReferenceOptions> options) : IBasketballReferenceRateLimiter
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastRequestAtUtc = DateTime.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.MinRequestIntervalSeconds));
            var remaining = interval - (DateTime.UtcNow - lastRequestAtUtc);
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, cancellationToken);
            }

            lastRequestAtUtc = DateTime.UtcNow;
        }
        finally
        {
            gate.Release();
        }
    }
}
