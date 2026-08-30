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
        var service = new ModelLabRunService(dbContext, backtest);
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
        var backtest = new FakeBacktestService();
        var service = new ModelLabRunService(dbContext, backtest);
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
    }

    [Fact]
    public async Task ProcessorLinksQueuedRunOnlyOnce()
    {
        await using var dbContext = CreateContext();
        var (ownerId, model, version) = await SeedModelAsync(dbContext);
        var service = new ModelLabRunService(dbContext, new FakeBacktestService());
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

    private static ModelLabEntitlement PaidEntitlement()
        => new("paid", true, true, 20, 100, null);

    private sealed class RecordingDispatcher : IModelLabJobDispatcher
    {
        public List<Guid> RunIds { get; } = [];

        public string EnqueueRun(Guid runId)
        {
            RunIds.Add(runId);
            return $"model-lab-job-{RunIds.Count}";
        }
    }

    private sealed class FakeBacktestService : IModelLabBacktestService
    {
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
                [],
                [],
                [],
                EloPoolKey: EloPoolKeys.Nba);
            return Task.FromResult(new ModelLabBacktestExecutionResult(response, [], [], [], []));
        }
    }
}
