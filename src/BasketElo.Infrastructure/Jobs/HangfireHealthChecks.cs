using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BasketElo.Infrastructure.Jobs;

public sealed class HangfireStorageHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var queues = JobStorage.Current.GetMonitoringApi().Queues();
            return Task.FromResult(HealthCheckResult.Healthy(
                $"Hangfire storage is reachable; {queues.Count} queue(s) visible."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Hangfire storage is not reachable.",
                exception));
        }
    }
}

public sealed class HangfireServerHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var servers = JobStorage.Current.GetMonitoringApi().Servers();
            return Task.FromResult(servers.Count > 0
                ? HealthCheckResult.Healthy($"{servers.Count} Hangfire server(s) active.")
                : HealthCheckResult.Unhealthy("No active Hangfire servers are registered."));
        }
        catch (Exception exception)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Hangfire server state could not be read.",
                exception));
        }
    }
}
