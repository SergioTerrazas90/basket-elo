using BasketElo.Domain.CurrentResults;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.CurrentResults;

public sealed class CurrentResultsSchedulerService(
    BasketEloDbContext dbContext,
    ICurrentResultsIngestionService ingestionService,
    IOptions<CurrentResultsOptions> options,
    TimeProvider timeProvider) : ICurrentResultsSchedulerService
{
    public async Task<(bool Queued, DateOnly FromDate, DateOnly ToDate, string? Status)> QueueIfDueAsync(CancellationToken cancellationToken)
    {
        var configuration = options.Value;
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var fromDate = today.AddDays(-Math.Max(0, configuration.ReconcileDaysBack));
        var toDate = today.AddDays(Math.Max(0, configuration.ScheduleDaysAhead));

        if (now.Hour < Math.Clamp(configuration.DailyReadHourUtc, 0, 23))
        {
            return (false, fromDate, toDate, "waiting_for_daily_read_hour");
        }

        var alreadyCompleted = await dbContext.CurrentResultsRuns
            .AsNoTracking()
            .AnyAsync(x => x.Provider == configuration.Provider &&
                x.FromDate == fromDate &&
                x.ToDate == toDate &&
                x.StartedAtUtc >= now.UtcDateTime.Date &&
                (x.Status == "completed" || x.Status == "dry_run"), cancellationToken);
        if (alreadyCompleted)
        {
            return (false, fromDate, toDate, "already_completed");
        }

        var summary = await ingestionService.RunAsync(fromDate, toDate, configuration.DryRun, cancellationToken);
        return (true, fromDate, toDate, summary.Status);
    }
}
