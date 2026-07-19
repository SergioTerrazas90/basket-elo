using System.Text.Json;
using BasketElo.Domain.Elo;
using Npgsql;

namespace BasketElo.Api.Elo;

public sealed class PostgresEloRebuildCacheInvalidationListener(
    IConfiguration configuration,
    EloResponseCache responseCache,
    ILogger<PostgresEloRebuildCacheInvalidationListener> logger) : BackgroundService
{
    private readonly string connectionString = configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ELO response cache invalidation listener disconnected; retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        connection.Notification += (_, args) => HandleNotification(args.Payload);

        await connection.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand($"listen {EloRebuildNotifications.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation("Listening for ELO cache invalidation notifications on {channel}.", EloRebuildNotifications.Channel);

        while (!cancellationToken.IsCancellationRequested)
        {
            await connection.WaitAsync(cancellationToken);
        }
    }

    private void HandleNotification(string payload)
    {
        try
        {
            var notification = JsonSerializer.Deserialize<EloRebuildRunNotification>(payload);
            if (notification is not null)
            {
                responseCache.Invalidate(notification);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not invalidate ELO response cache from notification payload: {payload}", payload);
        }
    }
}
