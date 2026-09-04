using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Jobs;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class ModelLabAsyncRunTests
{
    [Fact]
    public async Task CreateReturnsQueuedWithoutRunningBacktestAndRejectsSecondActiveRun()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var backtest = new FakeBacktestService();
        var service = new ModelLabRunService(dbContext, backtest, new RecordingDispatcher());
        var entitlement = PaidEntitlement();
        var request = CreateRequest(model.Id, version.Id);

        var created = await service.CreateAsync(ownerId, entitlement, request, CancellationToken.None);

        Assert.NotNull(created);
        Assert.Equal(ModelLabRunStatuses.Queued, created.Status);
        Assert.Equal(1, created.QueuePosition);
        Assert.Null(created.Result);
        Assert.Equal(0, backtest.ExecutionCount);
        var exception = await Assert.ThrowsAsync<ModelLabLimitException>(() =>
            service.CreateAsync(ownerId, entitlement, request, CancellationToken.None));
        Assert.Equal("model_lab_run_already_active", exception.Code);
    }

    [Fact]
    public async Task HangfireJobClaimsRunAndPersistsCompletedResult()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var canonicalRating = new TeamRating
        {
            TeamId = Guid.NewGuid(),
            EloPoolKey = EloPoolKeys.Nba,
            RulesetVersion = "canonical-v1",
            Elo = 1610m,
            GamesPlayed = 12
        };
        dbContext.TeamRatings.Add(canonicalRating);
        await dbContext.SaveChangesAsync();
        var backtest = new FakeBacktestService();
        var service = new ModelLabRunService(dbContext, backtest, new RecordingDispatcher());
        var created = await service.CreateAsync(
            ownerId,
            PaidEntitlement(),
            CreateRequest(model.Id, version.Id),
            CancellationToken.None);
        Assert.NotNull(created);
        dbContext.ChangeTracker.Clear();
        var job = new ModelLabRunJob(
            dbContext,
            service,
            NullLogger<ModelLabRunJob>.Instance);

        await job.ExecuteAsync(created.RunId, CancellationToken.None);

        Assert.Equal(1, backtest.ExecutionCount);
        var run = await dbContext.ModelLabRuns.AsNoTracking().SingleAsync(x => x.Id == created.RunId);
        Assert.Equal(ModelLabRunStatuses.Completed, run.Status);
        Assert.Equal(100, run.ProgressPercent);
        Assert.NotNull(run.StartedAtUtc);
        Assert.NotNull(run.CompletedAtUtc);
        Assert.Equal(1, run.ScoredGames);
        Assert.Single(await dbContext.ModelLabRunEvolutionPoints.AsNoTracking().ToListAsync());
        var evolution = await service.GetEvolutionAsync(
            ownerId,
            created.RunId,
            10,
            EloEvolutionLimits.DefaultPointsPerTeam,
            CancellationToken.None);
        Assert.NotNull(evolution);
        Assert.Single(evolution.Series);
        Assert.Null(await service.GetEvolutionAsync(
            Guid.NewGuid(),
            created.RunId,
            10,
            EloEvolutionLimits.DefaultPointsPerTeam,
            CancellationToken.None));
        var unchangedCanonicalRating = await dbContext.TeamRatings.AsNoTracking().SingleAsync();
        Assert.Equal(1610m, unchangedCanonicalRating.Elo);
        Assert.Equal(12, unchangedCanonicalRating.GamesPlayed);
        Assert.Empty(await dbContext.RatingHistories.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ProcessorLinksQueuedRunOnlyOnce()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), new RecordingDispatcher());
        var created = await service.CreateAsync(
            ownerId,
            PaidEntitlement(),
            CreateRequest(model.Id, version.Id),
            CancellationToken.None);
        Assert.NotNull(created);
        dbContext.ChangeTracker.Clear();
        var dispatcher = new RecordingDispatcher();
        var processor = new ModelLabRunJobProcessor(
            dbContext,
            dispatcher,
            NullLogger<ModelLabRunJobProcessor>.Instance);

        Assert.True(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));
        Assert.False(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));

        Assert.Equal(created.RunId, Assert.Single(dispatcher.RunIds));
        Assert.Equal("model-lab-job-1", (await dbContext.ModelLabRuns.SingleAsync()).HangfireJobId);
    }

    [Fact]
    public async Task RunHistoryFiltersByOwnerAndModel()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var otherModelId = Guid.NewGuid();
        dbContext.ModelLabRuns.AddRange(
            CompletedRun(ownerId, model.Id, version.Id, "Owned target"),
            CompletedRun(ownerId, otherModelId, version.Id, "Different model"),
            CompletedRun(Guid.NewGuid(), model.Id, version.Id, "Different owner"));
        await dbContext.SaveChangesAsync();
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), new RecordingDispatcher());

        var runs = await service.ListAsync(ownerId, 100, model.Id, CancellationToken.None);

        var run = Assert.Single(runs);
        Assert.Equal("Owned target", run.ModelName);
        Assert.Equal(ownerId, (await dbContext.ModelLabRuns.SingleAsync(x => x.Id == run.Id)).OwnerUserId);
    }

    [Fact]
    public async Task LatestCompatibleComparisonRequiresExactModelsAndCurrentVersions()
    {
        await using var dbContext = CreateContext();
        var (ownerId, firstModel, firstVersion) = await SeedModelAsync(dbContext);
        var secondModel = new ModelLabModel { OwnerUserId = ownerId, Name = "Second model", LeagueName = "" };
        var secondVersion = new ModelLabModelVersion
        {
            Model = secondModel,
            VersionNumber = 1,
            BaseRating = 1500m,
            KFactor = 25,
            HomeAdvantageElo = 90m,
            ProbabilityScale = 400m,
            UsesMarginAdjustment = true,
            PointsPerEloMargin = 20m,
            CompetitionWeight = 1m
        };
        secondModel.Versions.Add(secondVersion);
        dbContext.ModelLabModels.Add(secondModel);
        await dbContext.SaveChangesAsync();

        var comparisonGroupId = Guid.NewGuid();
        var firstRun = CompletedRun(ownerId, firstModel.Id, firstVersion.Id, firstModel.Name);
        var secondRun = CompletedRun(ownerId, secondModel.Id, secondVersion.Id, secondModel.Name);
        firstRun.ComparisonGroupId = comparisonGroupId;
        secondRun.ComparisonGroupId = comparisonGroupId;
        dbContext.ModelLabRuns.AddRange(firstRun, secondRun);
        await dbContext.SaveChangesAsync();
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), new RecordingDispatcher());

        var compatible = await service.GetLatestCompatibleComparisonAsync(
            ownerId,
            [firstModel.Id, secondModel.Id],
            CancellationToken.None);

        Assert.NotNull(compatible);
        Assert.Equal(comparisonGroupId, compatible.ComparisonGroupId);
        Assert.Equal(2, compatible.Runs.Count);
        var available = await service.ListCompatibleComparisonsAsync(ownerId, 6, CancellationToken.None);
        Assert.Equal(comparisonGroupId, Assert.Single(available).ComparisonGroupId);

        dbContext.ChangeTracker.Clear();
        dbContext.ModelLabModelVersions.Add(new ModelLabModelVersion
        {
            ModelId = firstModel.Id,
            VersionNumber = 2,
            BaseRating = 1500m,
            KFactor = 30,
            HomeAdvantageElo = 100m,
            ProbabilityScale = 400m,
            UsesMarginAdjustment = true,
            PointsPerEloMargin = 20m,
            CompetitionWeight = 1m
        });
        await dbContext.SaveChangesAsync();

        Assert.Null(await service.GetLatestCompatibleComparisonAsync(
            ownerId,
            [firstModel.Id, secondModel.Id],
            CancellationToken.None));
        Assert.Empty(await service.ListCompatibleComparisonsAsync(ownerId, 6, CancellationToken.None));
    }

    [Fact]
    public async Task DeletingCanceledRunDoesNotRestoreMonthlyAllowance()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var dispatcher = new RecordingDispatcher();
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), dispatcher);
        var request = CreateRequest(model.Id, version.Id);
        var first = await service.CreateAsync(ownerId, PaidEntitlement(), request, CancellationToken.None);
        Assert.NotNull(first);
        var stored = await dbContext.ModelLabRuns.SingleAsync(x => x.Id == first.RunId);
        stored.HangfireJobId = "queued-job";
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.DeleteAsync(ownerId, first.RunId, CancellationToken.None));
        var canceled = await service.CancelAsync(ownerId, first.RunId, CancellationToken.None);
        var deleted = await service.DeleteAsync(ownerId, first.RunId, CancellationToken.None);
        var second = await service.CreateAsync(ownerId, PaidEntitlement(), request, CancellationToken.None);

        Assert.Equal(ModelLabRunStatuses.Canceled, canceled?.Status);
        Assert.True(deleted);
        Assert.Contains("queued-job", dispatcher.DeletedJobIds);
        Assert.Equal(ModelLabRunStatuses.Queued, second?.Status);
        Assert.Equal(2, await dbContext.ModelLabMonthlyRunUsages.CountAsync(x => x.OwnerUserId == ownerId));
    }

    [Fact]
    public async Task PremiumMonthlyLimitRejectsRunTwoHundredAndOne()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        dbContext.ModelLabMonthlyRunUsages.AddRange(Enumerable.Range(1, 200).Select(slot =>
            new ModelLabMonthlyRunUsage
            {
                OwnerUserId = ownerId,
                MonthStartUtc = currentMonth,
                SlotNumber = slot,
                RunId = Guid.NewGuid(),
                UsageType = ModelLabRunUsageTypes.Run,
                CreatedAtUtc = currentMonth
            }));
        await dbContext.SaveChangesAsync();
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), new RecordingDispatcher());

        var exception = await Assert.ThrowsAsync<ModelLabLimitException>(() => service.CreateAsync(
            ownerId,
            PaidEntitlement(),
            CreateRequest(model.Id, version.Id),
            CancellationToken.None));

        Assert.Equal("monthly_run_limit_reached", exception.Code);
        Assert.Equal(200, exception.MonthlyRunLimit);
        var quota = await service.GetQuotaAsync(ownerId, PaidEntitlement(), CancellationToken.None);
        Assert.Equal(200, quota.MonthlyRuns);
        Assert.True(quota.IsMonthlyLimitReached);
    }

    [Fact]
    public async Task PremiumAllowanceUsesSubscriptionAnniversaryWindowInsteadOfCalendarMonth()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-20);
        var windowEnd = windowStart.AddMonths(1);
        var entitlement = PaidEntitlement() with
        {
            MonthlyRunLimit = 1,
            MonthlyRunWindowStartUtc = windowStart,
            MonthlyRunWindowEndUtc = windowEnd
        };
        dbContext.ModelLabMonthlyRunUsages.Add(new ModelLabMonthlyRunUsage
        {
            OwnerUserId = ownerId,
            MonthStartUtc = windowStart,
            SlotNumber = 1,
            RunId = Guid.NewGuid(),
            UsageType = ModelLabRunUsageTypes.Run,
            CreatedAtUtc = now.AddDays(-15)
        });
        await dbContext.SaveChangesAsync();
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), new RecordingDispatcher());

        var exception = await Assert.ThrowsAsync<ModelLabLimitException>(() => service.CreateAsync(
            ownerId,
            entitlement,
            CreateRequest(model.Id, version.Id),
            CancellationToken.None));

        Assert.Equal("monthly_run_limit_reached", exception.Code);
        var quota = await service.GetQuotaAsync(ownerId, entitlement, CancellationToken.None);
        Assert.Equal(1, quota.MonthlyRuns);
        Assert.Equal(windowEnd, quota.MonthlyLimitResetsAtUtc);
    }

    [Fact]
    public async Task ComparisonCountsEveryModelAndRejectsBeforeCreatingPartialRuns()
    {
        await using var dbContext = CreateContext();
        var (ownerId, firstModel, _) = await SeedModelAsync(dbContext);
        var secondModel = await SeedAdditionalModelAsync(dbContext, ownerId, "Second model");
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        dbContext.ModelLabMonthlyRunUsages.AddRange(Enumerable.Range(1, 199).Select(slot =>
            new ModelLabMonthlyRunUsage
            {
                OwnerUserId = ownerId,
                MonthStartUtc = currentMonth,
                SlotNumber = slot,
                RunId = Guid.NewGuid(),
                UsageType = ModelLabRunUsageTypes.Run,
                CreatedAtUtc = currentMonth
            }));
        await dbContext.SaveChangesAsync();
        var service = new ModelLabRunService(dbContext, new FakeBacktestService(), new RecordingDispatcher());
        var request = new CreateModelLabComparisonRequest(
            [firstModel.Id, secondModel.Id],
            currentMonth.AddMonths(-2),
            currentMonth.AddMonths(-1).AddTicks(-1),
            currentMonth.AddMonths(-1),
            currentMonth.AddTicks(-1),
            ModelLabScopeTypes.AllCompetitions,
            [],
            EloPoolKeys.Nba,
            "NBA");

        var exception = await Assert.ThrowsAsync<ModelLabLimitException>(() =>
            service.CreateComparisonAsync(ownerId, PaidEntitlement(), request, CancellationToken.None));

        Assert.Equal("monthly_run_limit_reached", exception.Code);
        Assert.Empty(await dbContext.ModelLabRuns.ToListAsync());
        Assert.Equal(199, await dbContext.ModelLabMonthlyRunUsages.CountAsync());
    }

    [Fact]
    public async Task QuotaRetentionRetryAndExpiryDoNotRecalculate()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var backtest = new FakeBacktestService();
        var service = new ModelLabRunService(dbContext, backtest, new RecordingDispatcher());
        var retained = CompletedRun(ownerId, model.Id, version.Id, "Retained");
        var failed = CompletedRun(ownerId, model.Id, version.Id, "Failed");
        failed.Status = ModelLabRunStatuses.Failed;
        var temporary = CompletedRun(ownerId, model.Id, version.Id, "Temporary");
        temporary.IsRetained = false;
        temporary.ExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        var expired = CompletedRun(Guid.NewGuid(), model.Id, version.Id, "Expired");
        expired.IsRetained = false;
        expired.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        dbContext.ModelLabRuns.AddRange(retained, failed, temporary, expired);
        await dbContext.SaveChangesAsync();

        var quotaBefore = await service.GetQuotaAsync(ownerId, PaidEntitlement(), CancellationToken.None);
        var retainedTemporary = await service.RetainAsync(ownerId, temporary.Id, PaidEntitlement(), CancellationToken.None);
        var retried = await service.RetryAsync(ownerId, failed.Id, PaidEntitlement(), CancellationToken.None);
        var deleted = await service.CleanupExpiredTemporaryRunsAsync(CancellationToken.None);
        var quotaAfter = await service.GetQuotaAsync(ownerId, PaidEntitlement(), CancellationToken.None);

        Assert.Equal(1, quotaBefore.StoredRuns);
        Assert.True(retainedTemporary?.IsRetained);
        Assert.Equal(ModelLabRunStatuses.Queued, retried?.Status);
        Assert.Equal(1, deleted);
        Assert.Equal(2, quotaAfter.StoredRuns);
        Assert.Equal(0, backtest.ExecutionCount);
    }

    private static BasketEloDbContext CreateContext(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString())
            .Options;
        return new BasketEloDbContext(options);
    }

    private static async Task<(Guid OwnerId, ModelLabModel Model, ModelLabModelVersion Version)> SeedModelAsync(
        BasketEloDbContext dbContext)
    {
        var ownerId = Guid.NewGuid();
        var model = new ModelLabModel
        {
            OwnerUserId = ownerId,
            Name = "Queued model",
            LeagueName = "NBA"
        };
        var version = new ModelLabModelVersion
        {
            Model = model,
            VersionNumber = 1,
            BaseRating = 1500m,
            KFactor = 20,
            HomeAdvantageElo = 100m,
            ProbabilityScale = 400m,
            UsesMarginAdjustment = true,
            PointsPerEloMargin = 20m,
            CompetitionWeight = 1m
        };
        model.Versions.Add(version);
        dbContext.ModelLabModels.Add(model);
        await dbContext.SaveChangesAsync();
        return (ownerId, model, version);
    }

    private static async Task<ModelLabModel> SeedAdditionalModelAsync(
        BasketEloDbContext dbContext,
        Guid ownerId,
        string name)
    {
        var model = new ModelLabModel
        {
            OwnerUserId = ownerId,
            Name = name,
            LeagueName = "NBA"
        };
        model.Versions.Add(new ModelLabModelVersion
        {
            Model = model,
            VersionNumber = 1,
            BaseRating = 1500m,
            KFactor = 20,
            HomeAdvantageElo = 100m,
            ProbabilityScale = 400m,
            UsesMarginAdjustment = true,
            PointsPerEloMargin = 20m,
            CompetitionWeight = 1m
        });
        dbContext.ModelLabModels.Add(model);
        await dbContext.SaveChangesAsync();
        return model;
    }

    private static CreateModelLabRunRequest CreateRequest(Guid modelId, Guid versionId)
        => new(
            modelId,
            versionId,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Utc),
            ModelLabScopeTypes.AllCompetitions,
            [],
            EloPoolKeys.Nba);

    private static ModelLabRun CompletedRun(Guid ownerId, Guid modelId, Guid versionId, string name) => new()
    {
        OwnerUserId = ownerId,
        ModelId = modelId,
        ModelVersionId = versionId,
        ModelName = name,
        LeagueName = "NBA",
        EloPoolKey = EloPoolKeys.Nba,
        ScopeType = ModelLabScopeTypes.AllCompetitions,
        Status = ModelLabRunStatuses.Completed,
        ProgressPercent = 100,
        ProgressStage = "Completed",
        InitializationFromUtc = DateTime.UtcNow.AddMonths(-2),
        InitializationToUtc = DateTime.UtcNow.AddMonths(-1),
        ScoredFromUtc = DateTime.UtcNow.AddMonths(-1),
        ScoredToUtc = DateTime.UtcNow,
        CompletedAtUtc = DateTime.UtcNow
    };

    private static ModelLabEntitlement PaidEntitlement()
        => new("paid", true, true, 20, 100, 200, null);

    private sealed class RecordingDispatcher : IModelLabJobDispatcher
    {
        public List<Guid> RunIds { get; } = [];
        public List<string> DeletedJobIds { get; } = [];

        public string EnqueueRun(Guid runId)
        {
            RunIds.Add(runId);
            return $"model-lab-job-{RunIds.Count}";
        }

        public bool Delete(string jobId)
        {
            DeletedJobIds.Add(jobId);
            return true;
        }
    }

    private sealed class FakeBacktestService : IModelLabBacktestService
    {
        private static readonly Guid TeamId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid GameId = Guid.Parse("20000000-0000-0000-0000-000000000001");

        public int ExecutionCount { get; private set; }

        public Task<ModelLabOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModelLabBacktestResponse> RunAsync(
            ModelLabBacktestRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ModelLabBacktestExecutionResult> RunDetailedAsync(
            ModelLabBacktestRequest request,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            var summary = new ModelLabBacktestSummary(1, 1, 100m, 0.1m, 0.2m, 3m, 55m);
            var response = new ModelLabBacktestResponse(
                request.ModelName ?? "Model",
                "All NBA competitions",
                request.Parameters,
                new ModelLabBacktestWindow(request.InitializationFromUtc, request.InitializationToUtc, 2),
                new ModelLabBacktestWindow(request.ScoredFromUtc, request.ScoredToUtc, 1),
                summary,
                summary,
                [new ModelLabRatingRow(1, TeamId, "Top team", 1512m, 1, 12m)],
                [],
                [],
                EloPoolKey: EloPoolKeys.Nba);
            var evolution = new ModelLabEvolutionSnapshot(
                TeamId,
                "Top team",
                GameId,
                request.ScoredFromUtc,
                "NBA",
                "2024-2025",
                1512m,
                12m,
                1);
            return Task.FromResult(new ModelLabBacktestExecutionResult(
                response,
                [],
                [],
                [],
                response.Ratings,
                response.Ratings,
                [evolution]));
        }
    }
}
