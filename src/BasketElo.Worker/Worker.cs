using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Elo;

namespace BasketElo.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var eloProcessor = scope.ServiceProvider.GetRequiredService<IEloRebuildJobProcessor>();
            var backfillProcessor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
            var processed = await eloProcessor.TryProcessNextPendingJobAsync(stoppingToken) ||
                await backfillProcessor.TryProcessNextPendingJobAsync(stoppingToken);

            if (_logger.IsEnabled(LogLevel.Information) && !processed)
            {
                _logger.LogInformation("Worker heartbeat at {time}", DateTimeOffset.UtcNow);
            }

            await Task.Delay(TimeSpan.FromSeconds(processed ? 2 : 5), stoppingToken);
        }
    }
}
