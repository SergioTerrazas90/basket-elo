using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Elo;

namespace BasketElo.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NbaRefreshOptions _nbaRefreshOptions;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<NbaRefreshOptions> nbaRefreshOptions)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _nbaRefreshOptions = nbaRefreshOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRefreshCheckUtc = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var eloProcessor = scope.ServiceProvider.GetRequiredService<IEloRebuildJobProcessor>();
            var backfillProcessor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
            var refreshQueued = false;
            if (_nbaRefreshOptions.Enabled && DateTime.UtcNow >= nextRefreshCheckUtc)
            {
                try
                {
                    var refreshService = scope.ServiceProvider.GetRequiredService<INbaCurrentSeasonRefreshService>();
                    var refresh = await refreshService.QueueIfDueAsync(stoppingToken);
                    refreshQueued = refresh.Queued;
                    if (refreshQueued)
                    {
                        _logger.LogInformation("Queued scheduled NBA refresh for {season}.", refresh.Season);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Scheduled NBA refresh check failed.");
                }

                nextRefreshCheckUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _nbaRefreshOptions.SchedulerCheckMinutes));
            }

            var processed = refreshQueued ||
                await eloProcessor.TryProcessNextPendingJobAsync(stoppingToken) ||
                await backfillProcessor.TryProcessNextPendingJobAsync(stoppingToken);

            if (_logger.IsEnabled(LogLevel.Information) && !processed)
            {
                _logger.LogInformation("Worker heartbeat at {time}", DateTimeOffset.UtcNow);
            }

            await Task.Delay(TimeSpan.FromSeconds(processed ? 2 : 5), stoppingToken);
        }
    }
}
