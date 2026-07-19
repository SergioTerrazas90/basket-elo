using BasketElo.Api.Controllers;
using BasketElo.Api.Elo;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloRebuildControllerTests
{
    [Fact]
    public async Task RankingsWithoutPoolDefaultToNba()
    {
        await using var dbContext = CreateDbContext();
        var nbaTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Boston Celtics", CountryCode = "USA" };
        var europeTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Real Madrid", CountryCode = "ES" };
        dbContext.Teams.AddRange(nbaTeam, europeTeam);
        dbContext.TeamRatings.AddRange(
            new TeamRating
            {
                TeamId = nbaTeam.Id,
                Team = nbaTeam,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Elo = 1600m
            },
            new TeamRating
            {
                TeamId = europeTeam.Id,
                Team = europeTeam,
                EloPoolKey = EloPoolKeys.EuropeClubs,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Elo = 1700m
            });
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext, new ScopedIdentityHealthService(EloPoolKeys.Nba));

        var result = await controller.GetRankings(
            rulesetVersion: null,
            pool: null,
            country: null,
            competition: null,
            season: null,
            fromUtc: null,
            toUtc: null,
            asOfDate: null,
            minGames: null,
            team: null);

        var response = Assert.IsType<EloRankingsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(EloPoolKeys.Nba, response.EloPoolKey);
        Assert.Collection(response.Rankings, row => Assert.Equal(nbaTeam.Id, row.TeamId));
    }

    [Fact]
    public async Task RankingsRejectCompetitionFromAnotherPool()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Competitions.Add(new Competition
        {
            Id = Guid.NewGuid(),
            Name = "ACB",
            Type = "league",
            CountryCode = "ES",
            EloPoolKey = EloPoolKeys.EuropeClubs
        });
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext, new ScopedIdentityHealthService(EloPoolKeys.Nba));

        var result = await controller.GetRankings(
            rulesetVersion: null,
            pool: EloPoolKeys.Nba,
            country: null,
            competition: "ACB",
            season: null,
            fromUtc: null,
            toUtc: null,
            asOfDate: null,
            minGames: null,
            team: null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task NbaRankingsDefaultToCurrentFranchisesAndCanIncludeHistoricalFranchises()
    {
        await using var dbContext = CreateDbContext();
        var lakers = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Los Angeles Lakers",
            CountryCode = "USA",
            IsActive = true
        };
        var huskies = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Toronto Huskies",
            CountryCode = "CAN",
            IsActive = false
        };
        dbContext.Teams.AddRange(lakers, huskies);
        dbContext.TeamRatings.AddRange(
            new TeamRating
            {
                TeamId = lakers.Id,
                Team = lakers,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Elo = 1600m
            },
            new TeamRating
            {
                TeamId = huskies.Id,
                Team = huskies,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Elo = 1700m
            });
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext, new ScopedIdentityHealthService(EloPoolKeys.Nba));

        var currentResult = await controller.GetRankings(
            rulesetVersion: null,
            pool: EloPoolKeys.Nba,
            country: null,
            competition: null,
            season: null,
            fromUtc: null,
            toUtc: null,
            asOfDate: null,
            minGames: null,
            team: null);

        var current = Assert.IsType<EloRankingsResponse>(Assert.IsType<OkObjectResult>(currentResult.Result).Value);
        Assert.Equal(EloNbaTeamScopes.Current, current.TeamScope);
        var currentRow = Assert.Single(current.Rankings);
        Assert.Equal(lakers.Id, currentRow.TeamId);
        Assert.True(currentRow.IsActive);

        var historicalResult = await controller.GetRankings(
            rulesetVersion: null,
            pool: EloPoolKeys.Nba,
            country: null,
            competition: null,
            season: null,
            fromUtc: null,
            toUtc: null,
            asOfDate: null,
            minGames: null,
            team: null,
            teams: EloNbaTeamScopes.Historical);

        var historical = Assert.IsType<EloRankingsResponse>(Assert.IsType<OkObjectResult>(historicalResult.Result).Value);
        Assert.Equal(EloNbaTeamScopes.Historical, historical.TeamScope);
        Assert.Equal(2, historical.Rankings.Count);
        Assert.False(historical.Rankings.Single(row => row.TeamId == huskies.Id).IsActive);
    }

    [Fact]
    public async Task EuropeanClubRankingsDefaultToCurrentTeamsAndCanIncludeHistoricalTeams()
    {
        await using var dbContext = CreateDbContext();
        var currentClub = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Real Madrid",
            CountryCode = "ES",
            IsActive = true
        };
        var relegatedClub = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "Historical Club",
            CountryCode = "ES",
            IsActive = true
        };
        var acb = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "ACB",
            Type = "league",
            CountryCode = "ES",
            EloPoolKey = EloPoolKeys.EuropeClubs
        };
        var latestSeason = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = acb.Id,
            Competition = acb,
            Label = "2025-2026",
            StartDateUtc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };
        var oldSeason = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = acb.Id,
            Competition = acb,
            Label = "2013-2014",
            StartDateUtc = new DateTime(2013, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2014, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };
        dbContext.Teams.AddRange(currentClub, relegatedClub);
        dbContext.Competitions.Add(acb);
        dbContext.Seasons.AddRange(latestSeason, oldSeason);
        dbContext.Games.AddRange(
            new Game
            {
                Id = Guid.NewGuid(),
                CompetitionId = acb.Id,
                Competition = acb,
                SeasonId = latestSeason.Id,
                Season = latestSeason,
                GameDateTimeUtc = new DateTime(2026, 1, 10, 19, 0, 0, DateTimeKind.Utc),
                HomeTeamId = currentClub.Id,
                AwayTeamId = currentClub.Id,
                Status = "finished"
            },
            new Game
            {
                Id = Guid.NewGuid(),
                CompetitionId = acb.Id,
                Competition = acb,
                SeasonId = oldSeason.Id,
                Season = oldSeason,
                GameDateTimeUtc = new DateTime(2014, 1, 10, 19, 0, 0, DateTimeKind.Utc),
                HomeTeamId = relegatedClub.Id,
                AwayTeamId = currentClub.Id,
                Status = "finished"
            });
        dbContext.TeamRatings.AddRange(
            new TeamRating
            {
                TeamId = currentClub.Id,
                Team = currentClub,
                EloPoolKey = EloPoolKeys.EuropeClubs,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Elo = 1600m
            },
            new TeamRating
            {
                TeamId = relegatedClub.Id,
                Team = relegatedClub,
                EloPoolKey = EloPoolKeys.EuropeClubs,
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                Elo = 1700m
            });
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext, new ScopedIdentityHealthService(EloPoolKeys.EuropeClubs));

        var currentResult = await controller.GetRankings(
            rulesetVersion: null,
            pool: EloPoolKeys.EuropeClubs,
            country: null,
            competition: null,
            season: null,
            fromUtc: null,
            toUtc: null,
            asOfDate: null,
            minGames: null,
            team: null);

        var current = Assert.IsType<EloRankingsResponse>(Assert.IsType<OkObjectResult>(currentResult.Result).Value);
        Assert.Equal(EloTeamScopes.Current, current.TeamScope);
        Assert.Collection(current.Rankings, row => Assert.Equal(currentClub.Id, row.TeamId));

        var historicalResult = await controller.GetRankings(
            rulesetVersion: null,
            pool: EloPoolKeys.EuropeClubs,
            country: null,
            competition: null,
            season: null,
            fromUtc: null,
            toUtc: null,
            asOfDate: null,
            minGames: null,
            team: null,
            teams: EloTeamScopes.Historical);

        var historical = Assert.IsType<EloRankingsResponse>(Assert.IsType<OkObjectResult>(historicalResult.Result).Value);
        Assert.Equal(EloTeamScopes.Historical, historical.TeamScope);
        Assert.Equal(2, historical.Rankings.Count);
        Assert.False(historical.Rankings.Single(row => row.TeamId == relegatedClub.Id).IsActive);
    }

    [Fact]
    public async Task PoolRebuildUsesPoolScopedIdentityGate()
    {
        await using var dbContext = CreateDbContext();
        var nba = new Competition { Id = Guid.NewGuid(), Name = "NBA", Type = "league", CountryCode = "USA", EloPoolKey = EloPoolKeys.Nba };
        dbContext.Competitions.Add(nba);
        await dbContext.SaveChangesAsync();
        var identityService = new ScopedIdentityHealthService(EloPoolKeys.Nba);
        var controller = CreateController(dbContext, identityService);

        var result = await controller.Rebuild(
            new EloRebuildRequest
            {
                RulesetVersion = EloRulesetVersions.AdjustedV1,
                PoolKey = EloPoolKeys.Nba
            },
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var runs = Assert.IsAssignableFrom<IReadOnlyList<EloRebuildRunDto>>(accepted.Value);
        Assert.Single(runs);
        Assert.Equal(EloPoolKeys.Nba, Assert.Single(identityService.Requests).EloPoolKey);
        var run = await dbContext.EloRebuildRuns.SingleAsync();
        Assert.Equal(EloPoolKeys.Nba, run.EloPoolKey);
        Assert.Equal(EloRebuildRunStatus.Pending, run.Status);
    }

    [Fact]
    public async Task PoolRebuildIsBlockedByPoolIdentityGate()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Competitions.Add(new Competition
        {
            Id = Guid.NewGuid(),
            Name = "NBA",
            Type = "league",
            CountryCode = "USA",
            EloPoolKey = EloPoolKeys.Nba
        });
        await dbContext.SaveChangesAsync();
        var identityService = new ScopedIdentityHealthService(EloPoolKeys.EuropeClubs);
        var controller = CreateController(dbContext, identityService);

        var result = await controller.Rebuild(
            new EloRebuildRequest { RulesetVersion = EloRulesetVersions.AdjustedV1 },
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(EloPoolKeys.Nba, Assert.Single(identityService.Requests).EloPoolKey);
        Assert.Empty(dbContext.EloRebuildRuns);
    }

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EloController CreateController(
        BasketEloDbContext dbContext,
        IIdentityHealthCheckService identityService)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new(
            dbContext,
            new NoOpNotificationPublisher(),
            identityService,
            memoryCache,
            new EloResponseCache(memoryCache, NullLogger<EloResponseCache>.Instance));
    }

    private sealed class ScopedIdentityHealthService(string cleanPoolKey) : IIdentityHealthCheckService
    {
        public List<IdentityHealthCheckRequest> Requests { get; } = [];

        public Task<IdentityHealthCheckRunDto> RunAsync(
            IdentityHealthCheckRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var blockers = request.EloPoolKey == cleanPoolKey ? 0 : 6;
            return Task.FromResult(new IdentityHealthCheckRunDto(
                Guid.NewGuid(),
                null,
                null,
                null,
                request.CompetitionId,
                $"source=*|season=*|country=*|competition=*|pool={request.EloPoolKey ?? "*"}",
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
