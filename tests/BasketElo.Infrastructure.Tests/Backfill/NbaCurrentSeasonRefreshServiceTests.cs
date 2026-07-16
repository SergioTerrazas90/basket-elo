using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaCurrentSeasonRefreshServiceTests
{
    [Theory]
    [InlineData("2026-01-15", "2025-2026")]
    [InlineData("2026-07-15", "2026-2027")]
    [InlineData("2026-10-15", "2026-2027")]
    public void ResolvesActiveSeasonAcrossCalendarBoundaries(string date, string expected)
    {
        Assert.Equal(expected, NbaCurrentSeasonRefreshService.GetCurrentSeason(DateTime.Parse(date).ToUniversalTime()));
    }

    [Fact]
    public async Task DisabledSchedulerDoesNotQueue()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, new DateTimeOffset(2026, 1, 15, 6, 0, 0, TimeSpan.Zero), enabled: false);

        var result = await service.QueueIfDueAsync(CancellationToken.None);

        Assert.False(result.Queued);
        Assert.Equal("disabled", result.Reason);
        Assert.Empty(dbContext.BackfillJobs);
    }

    [Fact]
    public async Task DueSchedulerQueuesProductionJobAndDeduplicatesActiveSeason()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(dbContext, new DateTimeOffset(2026, 1, 15, 6, 0, 0, TimeSpan.Zero));

        var first = await service.QueueIfDueAsync(CancellationToken.None);
        var duplicate = await service.QueueIfDueAsync(CancellationToken.None);

        Assert.True(first.Queued);
        Assert.Equal("scheduled", first.Reason);
        Assert.False(duplicate.Queued);
        Assert.Equal("active_job_exists", duplicate.Reason);
        var job = Assert.Single(await dbContext.BackfillJobs.ToListAsync());
        Assert.Equal("2025-2026", job.Season);
        Assert.Equal(ApiSportsBasketballDataProvider.Source, job.Provider);
        Assert.Equal("USA", job.Country);
        Assert.False(job.DryRun);
        Assert.Equal(8, job.MaxRequests);
    }

    [Fact]
    public async Task ManualQueueBypassesCadenceButStillDeduplicatesActiveJobs()
    {
        var now = new DateTimeOffset(2026, 1, 15, 6, 0, 0, TimeSpan.Zero);
        await using var dbContext = CreateDbContext();
        dbContext.BackfillJobs.Add(new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = ApiSportsBasketballDataProvider.Source,
            Country = "USA",
            LeagueName = "NBA",
            Season = "2025-2026",
            Status = BackfillJobStatus.Completed,
            CreatedAtUtc = now.UtcDateTime.AddHours(-1),
            UpdatedAtUtc = now.UtcDateTime.AddHours(-1)
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, now);

        var scheduled = await service.QueueIfDueAsync(CancellationToken.None);
        var manual = await service.QueueManualAsync(dryRun: true, maxRequests: 4, CancellationToken.None);
        var duplicate = await service.QueueManualAsync(dryRun: false, maxRequests: null, CancellationToken.None);

        Assert.False(scheduled.Queued);
        Assert.Equal("not_due", scheduled.Reason);
        Assert.NotNull(scheduled.NextDueAtUtc);
        Assert.True(manual.Queued);
        Assert.Equal("manual", manual.Reason);
        Assert.False(duplicate.Queued);
        Assert.Equal("active_job_exists", duplicate.Reason);
        var manualJob = await dbContext.BackfillJobs.SingleAsync(x => x.Id == manual.JobId);
        Assert.True(manualJob.DryRun);
        Assert.Equal(4, manualJob.MaxRequests);
    }

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static NbaCurrentSeasonRefreshService CreateService(
        BasketEloDbContext dbContext,
        DateTimeOffset now,
        bool enabled = true) =>
        new(
            dbContext,
            Options.Create(new NbaRefreshOptions
            {
                Enabled = enabled,
                InSeasonIntervalHours = 12,
                OffSeasonIntervalHours = 168,
                MaxRequests = 8,
                DryRun = false
            }),
            new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
