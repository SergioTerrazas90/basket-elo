using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public class ApiSportsRateLimiter(IOptions<ApiSportsOptions> options) : IApiSportsRateLimiter
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _nextRequestAtUtc = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextRequestAtUtc - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            _nextRequestAtUtc = DateTimeOffset.UtcNow.AddSeconds(options.Value.MinSecondsBetweenRequests);
        }
        finally
        {
            _gate.Release();
        }
    }
}
