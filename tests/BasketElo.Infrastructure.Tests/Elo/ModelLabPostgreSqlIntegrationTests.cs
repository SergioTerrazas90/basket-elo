using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class ModelLabPostgreSqlIntegrationTests
{
    [PostgreSqlIntegrationFact]
    public async Task ActiveRunConstraintAndAtomicClaimWorkWithFourUsers()
    {
        var sourceConnectionString = Environment.GetEnvironmentVariable("BASKETELO_TEST_POSTGRES")!;
        var databaseName = $"basket_elo_test_{Guid.NewGuid():N}";
        var maintenance = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = "postgres",
            Pooling = false
        };
        var testDatabase = new NpgsqlConnectionStringBuilder(sourceConnectionString)
        {
            Database = databaseName,
            Pooling = false
        };

        await using var maintenanceConnection = new NpgsqlConnection(maintenance.ConnectionString);
        await maintenanceConnection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", maintenanceConnection))
        {
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseNpgsql(testDatabase.ConnectionString)
                .Options;
            await using (var setup = new BasketEloDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                for (var index = 0; index < 4; index++)
                {
                    AddQueuedRun(setup, index);
                }

                await setup.SaveChangesAsync();
                var firstThreeIds = await setup.ModelLabRuns
                    .OrderBy(x => x.CreatedAtUtc)
                    .Take(3)
                    .Select(x => x.Id)
                    .ToListAsync();
                await setup.ModelLabRuns
                    .Where(x => firstThreeIds.Contains(x.Id))
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, ModelLabRunStatuses.Running));

                Assert.Equal(3, await setup.ModelLabRuns.CountAsync(x => x.Status == ModelLabRunStatuses.Running));
                Assert.Equal(1, await setup.ModelLabRuns.CountAsync(x => x.Status == ModelLabRunStatuses.Queued));
                Assert.Empty(await setup.TeamRatings.ToListAsync());
                Assert.Empty(await setup.RatingHistories.ToListAsync());
            }

            Guid queuedRunId;
            Guid queuedOwnerId;
            await using (var reader = new BasketEloDbContext(options))
            {
                var queued = await reader.ModelLabRuns.SingleAsync(x => x.Status == ModelLabRunStatuses.Queued);
                queuedRunId = queued.Id;
                queuedOwnerId = queued.OwnerUserId;
            }

            await using (var duplicate = new BasketEloDbContext(options))
            {
                var sourceRun = await duplicate.ModelLabRuns.AsNoTracking().SingleAsync(x => x.Id == queuedRunId);
                duplicate.ModelLabRuns.Add(CloneActiveRun(sourceRun, queuedOwnerId));
                await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
            }

            await using var firstWorker = new BasketEloDbContext(options);
            await using var secondWorker = new BasketEloDbContext(options);
            var firstClaim = await ClaimAsync(firstWorker, queuedRunId);
            var duplicateClaim = await ClaimAsync(secondWorker, queuedRunId);
            Assert.Equal(1, firstClaim);
            Assert.Equal(0, duplicateClaim);
            Assert.Empty(await firstWorker.TeamRatings.ToListAsync());
            Assert.Empty(await firstWorker.RatingHistories.ToListAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var terminate = new NpgsqlCommand(
                "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @databaseName",
                maintenanceConnection);
            terminate.Parameters.AddWithValue("databaseName", databaseName);
            await terminate.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\"", maintenanceConnection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static void AddQueuedRun(BasketEloDbContext dbContext, int index)
    {
        var owner = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            DisplayName = $"Test user {index}",
            Email = $"model-lab-{index}@integration.test",
            NormalizedEmail = $"MODEL-LAB-{index}@INTEGRATION.TEST"
        };
        var model = new ModelLabModel
        {
            OwnerUser = owner,
            Name = $"Model {index}",
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
        dbContext.ModelLabRuns.Add(new ModelLabRun
        {
            OwnerUser = owner,
            Model = model,
            ModelVersion = version,
            ModelName = model.Name,
            LeagueName = "NBA",
            EloPoolKey = EloPoolKeys.Nba,
            ScopeType = ModelLabScopeTypes.AllCompetitions,
            Status = ModelLabRunStatuses.Queued,
            ProgressStage = "Waiting for a worker",
            InitializationFromUtc = DateTime.UtcNow.AddMonths(-2),
            InitializationToUtc = DateTime.UtcNow.AddMonths(-1),
            ScoredFromUtc = DateTime.UtcNow.AddMonths(-1),
            ScoredToUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow.AddSeconds(index)
        });
    }

    private static ModelLabRun CloneActiveRun(ModelLabRun source, Guid ownerId) => new()
    {
        OwnerUserId = ownerId,
        ModelId = source.ModelId,
        ModelVersionId = source.ModelVersionId,
        ModelName = source.ModelName,
        LeagueName = source.LeagueName,
        EloPoolKey = source.EloPoolKey,
        ScopeType = source.ScopeType,
        Status = ModelLabRunStatuses.Queued,
        ProgressStage = "Waiting for a worker",
        InitializationFromUtc = source.InitializationFromUtc,
        InitializationToUtc = source.InitializationToUtc,
        ScoredFromUtc = source.ScoredFromUtc,
        ScoredToUtc = source.ScoredToUtc
    };

    private static Task<int> ClaimAsync(BasketEloDbContext dbContext, Guid runId) =>
        dbContext.ModelLabRuns
            .Where(x => x.Id == runId && x.Status == ModelLabRunStatuses.Queued)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ModelLabRunStatuses.Running)
                .SetProperty(x => x.ProgressStage, "Calculating ratings"));
}

public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BASKETELO_TEST_POSTGRES")))
        {
            Skip = "Set BASKETELO_TEST_POSTGRES to a PostgreSQL connection whose user may create test databases.";
        }
    }
}
