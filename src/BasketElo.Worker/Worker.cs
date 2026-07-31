using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.CurrentResults;
using BasketElo.Infrastructure.Elo;

namespace BasketElo.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NbaRefreshOptions _nbaRefreshOptions;
    private readonly CurrentResultsOptions _currentResultsOptions;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<NbaRefreshOptions> nbaRefreshOptions,
        Microsoft.Extensions.Options.IOptions<CurrentResultsOptions> currentResultsOptions)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _nbaRefreshOptions = nbaRefreshOptions.Value;
        _currentResultsOptions = currentResultsOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextRefreshCheckUtc = DateTime.MinValue;
        var nextCurrentResultsCheckUtc = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var eloProcessor = scope.ServiceProvider.GetRequiredService<IEloRebuildJobProcessor>();
            var backfillProcessor = scope.ServiceProvider.GetRequiredService<IBackfillJobProcessor>();
            var refreshQueued = false;
            var currentResultsQueued = false;
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

            if (_currentResultsOptions.Enabled && DateTime.UtcNow >= nextCurrentResultsCheckUtc)
            {
                try
                {
                    var currentResultsScheduler = scope.ServiceProvider.GetRequiredService<ICurrentResultsSchedulerService>();
                    var currentResults = await currentResultsScheduler.QueueIfDueAsync(stoppingToken);
                    currentResultsQueued = currentResults.Queued;
                    if (currentResultsQueued)
                    {
                        _logger.LogInformation("Completed current-results ingestion for {fromDate} through {toDate} with status {status}.", currentResults.FromDate, currentResults.ToDate, currentResults.Status);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Scheduled current-results ingestion failed.");
                }

                nextCurrentResultsCheckUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, _currentResultsOptions.SchedulerCheckMinutes));
            }

            var processed = refreshQueued || currentResultsQueued ||
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
