using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Backfill;

public sealed class NbaRefreshOptions
{
    public const string SectionName = "NbaRefresh";

    public bool Enabled { get; set; }
    public int InSeasonIntervalHours { get; set; } = 12;
    public int OffSeasonIntervalHours { get; set; } = 168;
    public int SchedulerCheckMinutes { get; set; } = 5;
    public int MaxRequests { get; set; } = 8;
    public bool DryRun { get; set; }
}

public sealed record NbaCurrentSeasonRefreshResult(
    string Season,
    bool Queued,
    string Reason,
    Guid? JobId,
    DateTime? NextDueAtUtc);

public interface INbaCurrentSeasonRefreshService
{
    Task<NbaCurrentSeasonRefreshResult> QueueIfDueAsync(CancellationToken cancellationToken);
    Task<NbaCurrentSeasonRefreshResult> QueueManualAsync(bool dryRun, int? maxRequests, CancellationToken cancellationToken);
}

public sealed class NbaCurrentSeasonRefreshService(
    BasketEloDbContext dbContext,
    IOptions<NbaRefreshOptions> options,
    TimeProvider timeProvider) : INbaCurrentSeasonRefreshService
{
    public Task<NbaCurrentSeasonRefreshResult> QueueIfDueAsync(CancellationToken cancellationToken) =>
        QueueAsync(force: false, options.Value.DryRun, options.Value.MaxRequests, cancellationToken);

    public Task<NbaCurrentSeasonRefreshResult> QueueManualAsync(
        bool dryRun,
        int? maxRequests,
        CancellationToken cancellationToken) =>
        QueueAsync(force: true, dryRun, maxRequests ?? options.Value.MaxRequests, cancellationToken);

    public static string GetCurrentSeason(DateTime utcNow)
    {
        var startYear = utcNow.Month >= 7 ? utcNow.Year : utcNow.Year - 1;
        return $"{startYear}-{startYear + 1}";
    }

    private async Task<NbaCurrentSeasonRefreshResult> QueueAsync(
        bool force,
        bool dryRun,
        int maxRequests,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var season = GetCurrentSeason(now);
        if (!force && !options.Value.Enabled)
        {
            return new(season, false, "disabled", null, null);
        }

        var activeJob = await dbContext.BackfillJobs
            .AsNoTracking()
            .Where(x =>
                x.Provider == BasketballReferenceBasketballDataProvider.Source &&
                x.Country == "United States" &&
                x.LeagueName == "NBA" &&
                x.Season == season &&
                (x.Status == BackfillJobStatus.Pending || x.Status == BackfillJobStatus.Running))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeJob is not null)
        {
            return new(season, false, "active_job_exists", activeJob.Id, null);
        }

        DateTime? nextDueAtUtc = null;
        if (!force)
        {
            var latestQueuedAtUtc = await dbContext.BackfillJobs
                .AsNoTracking()
                .Where(x =>
                    x.Provider == BasketballReferenceBasketballDataProvider.Source &&
                    x.Country == "United States" &&
                    x.LeagueName == "NBA" &&
                    x.Season == season)
                .MaxAsync(x => (DateTime?)x.CreatedAtUtc, cancellationToken);
            if (latestQueuedAtUtc.HasValue)
            {
                nextDueAtUtc = latestQueuedAtUtc.Value.Add(GetRefreshInterval(now));
                if (nextDueAtUtc > now)
                {
                    return new(season, false, "not_due", null, nextDueAtUtc);
                }
            }
        }

        var job = new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = BasketballReferenceBasketballDataProvider.Source,
            Country = "United States",
            LeagueName = "NBA",
            Season = season,
            DryRun = dryRun,
            MaxRequests = Math.Max(1, maxRequests),
            Status = BackfillJobStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        dbContext.BackfillJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(season, true, force ? "manual" : "scheduled", job.Id, null);
    }

    private TimeSpan GetRefreshInterval(DateTime utcNow)
    {
        var inSeason = utcNow.Month is >= 10 or <= 6;
        var hours = inSeason
            ? options.Value.InSeasonIntervalHours
            : options.Value.OffSeasonIntervalHours;
        return TimeSpan.FromHours(Math.Max(1, hours));
    }
}
