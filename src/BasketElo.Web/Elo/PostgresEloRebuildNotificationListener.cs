using System.Text.Json;
using BasketElo.Domain.Elo;
using Npgsql;

namespace BasketElo.Web.Elo;

public sealed class PostgresEloRebuildNotificationListener(
    IConfiguration configuration,
    EloRebuildNotificationCenter notificationCenter,
    ILogger<PostgresEloRebuildNotificationListener> logger) : BackgroundService
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres")
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
                logger.LogWarning(
                    ex,
                    "Postgres ELO rebuild notification listener disconnected; retrying.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        connection.Notification += (_, args) => _ = HandleNotificationAsync(args.Payload);

        await connection.OpenAsync(cancellationToken);
        await using (var command = new NpgsqlCommand($"listen {EloRebuildNotifications.Channel}", connection))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        logger.LogInformation(
            "Listening for Postgres notifications on {channel}.",
            EloRebuildNotifications.Channel);

        while (!cancellationToken.IsCancellationRequested)
        {
            await connection.WaitAsync(cancellationToken);
        }
    }

    private async Task HandleNotificationAsync(string payload)
    {
        try
        {
            var notification = JsonSerializer.Deserialize<EloRebuildRunNotification>(payload);
            if (notification is null)
            {
                logger.LogWarning("Received an empty ELO rebuild notification payload.");
                return;
            }

            logger.LogInformation(
                "Received ELO rebuild notification for run {runId} with status {status}.",
                notification.RunId,
                notification.Status);

            await notificationCenter.PublishAsync(notification);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not handle ELO rebuild notification payload: {payload}", payload);
        }
    }
}
