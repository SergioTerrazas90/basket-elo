using BasketElo.Api.Controllers;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloRebuildControllerTests
{
    [Fact]
    public async Task CompetitionRebuildUsesCompetitionScopedIdentityGate()
    {
        await using var dbContext = CreateDbContext();
        var nba = new Competition { Id = Guid.NewGuid(), Name = "NBA", Type = "league", CountryCode = "USA" };
        dbContext.Competitions.Add(nba);
        await dbContext.SaveChangesAsync();
        var identityService = new ScopedIdentityHealthService(nba.Id);
        var controller = CreateController(dbContext, identityService);

        var result = await controller.Rebuild(
            new EloRebuildRequest
            {
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                CompetitionName = "NBA"
            },
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var runs = Assert.IsAssignableFrom<IReadOnlyList<EloRebuildRunDto>>(accepted.Value);
        Assert.Single(runs);
        Assert.Equal(nba.Id, Assert.Single(identityService.Requests).CompetitionId);
        Assert.Equal(EloRebuildRunStatus.Pending, (await dbContext.EloRebuildRuns.SingleAsync()).Status);
    }

    [Fact]
    public async Task GlobalRebuildStillUsesGlobalIdentityGate()
    {
        await using var dbContext = CreateDbContext();
        var identityService = new ScopedIdentityHealthService(Guid.NewGuid());
        var controller = CreateController(dbContext, identityService);

        var result = await controller.Rebuild(
            new EloRebuildRequest { RulesetVersion = EloRulesetVersions.AdjustedV1 },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Null(Assert.Single(identityService.Requests).CompetitionId);
        Assert.Empty(dbContext.EloRebuildRuns);
    }

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EloController CreateController(
        BasketEloDbContext dbContext,
        IIdentityHealthCheckService identityService) =>
        new(
            dbContext,
            new NoOpNotificationPublisher(),
            identityService,
            new MemoryCache(new MemoryCacheOptions()));

    private sealed class ScopedIdentityHealthService(Guid cleanCompetitionId) : IIdentityHealthCheckService
    {
        public List<IdentityHealthCheckRequest> Requests { get; } = [];

        public Task<IdentityHealthCheckRunDto> RunAsync(
            IdentityHealthCheckRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var blockers = request.CompetitionId == cleanCompetitionId ? 0 : 6;
            return Task.FromResult(new IdentityHealthCheckRunDto(
                Guid.NewGuid(),
                null,
                null,
                null,
                request.CompetitionId,
                $"source=*|season=*|country=*|competition={request.CompetitionId?.ToString() ?? "*"}",
                "test-v1",
                blockers == 0 ? IdentityHealthCheckStatus.Clean : IdentityHealthCheckStatus.Blockers,
                blockers,
                blockers,
                blockers,
                0,
                blockers,
                0,
                0,
                [],
                false,
                DateTime.UtcNow,
                null));
        }

        public Task<IdentityHealthOptionsDto> GetOptionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityHealthCheckRunDto>> GetRunsAsync(IdentityHealthCheckQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityHealthCheckFindingDto>> GetFindingsAsync(IdentityFindingQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityHealthCheckFindingDto> ResolveFindingAsync(Guid findingId, ResolveIdentityFindingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRunAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateChangedScopeAsync(IdentityChangedScope changedScope, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpNotificationPublisher : IEloRebuildNotificationPublisher
    {
        public Task PublishAsync(EloRebuildRunNotification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
