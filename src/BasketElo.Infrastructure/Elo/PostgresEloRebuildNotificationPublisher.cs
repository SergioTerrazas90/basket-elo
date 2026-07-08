using System.Text.Json;
using BasketElo.Domain.Elo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BasketElo.Infrastructure.Elo;

public sealed class PostgresEloRebuildNotificationPublisher(
    IConfiguration configuration,
    ILogger<PostgresEloRebuildNotificationPublisher> logger) : IEloRebuildNotificationPublisher
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Port=5432;Database=basket_elo;Username=basket_elo;Password=basket_elo";

    public async Task PublishAsync(EloRebuildRunNotification notification, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(notification);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("select pg_notify(@channel, @payload)", connection);
            command.Parameters.AddWithValue("channel", EloRebuildNotifications.Channel);
            command.Parameters.AddWithValue("payload", payload);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not publish ELO rebuild notification for run {runId}.",
                notification.RunId);
        }
    }
}
