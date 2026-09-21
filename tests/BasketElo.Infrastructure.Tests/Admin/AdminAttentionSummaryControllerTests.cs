using BasketElo.Api.Controllers;
using BasketElo.Domain.Admin;
using BasketElo.Domain.CurrentResults;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Admin;

public sealed class AdminAttentionSummaryControllerTests
{
    [Fact]
    public async Task SummaryCountsOnlyActionableCurrentItems()
    {
        await using var dbContext = CreateDbContext();
        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "Test League",
            Type = "league",
            EloPoolKey = EloPoolKeys.EuropeClubs
        };
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Competition = competition,
            Label = "2026-2027"
        };
        var home = CreateTeam("Home", "ES");
        var away = CreateTeam("Away", "ES");
        var missingCountry = CreateTeam("Unknown", "UNK");
        var game = new Game
        {
            Id = Guid.NewGuid(),
            Source = "test",
            SourceGameId = "game-1",
            CompetitionId = competition.Id,
            Competition = competition,
            SeasonId = season.Id,
            Season = season,
            HomeTeamId = home.Id,
            HomeTeam = home,
            AwayTeamId = away.Id,
            AwayTeam = away,
            HomeScore = 81,
            AwayScore = 76,
            Status = "finished",
            EloEligible = true
        };
        var activeRun = new IdentityHealthCheckRun
        {
            Id = Guid.NewGuid(),
            ScopeKey = "all",
            Status = IdentityHealthCheckStatus.Blockers
        };
        var invalidatedRun = new IdentityHealthCheckRun
        {
            Id = Guid.NewGuid(),
            ScopeKey = "old",
            Status = IdentityHealthCheckStatus.Blockers,
            InvalidatedAtUtc = DateTime.UtcNow
        };

        dbContext.AddRange(competition, season, home, away, missingCountry, game, activeRun, invalidatedRun);
        dbContext.TeamAliases.AddRange(
            CreateAlias(home, "Shared name", "home-id"),
            CreateAlias(away, "shared NAME", "away-id"));
        dbContext.IdentityHealthCheckFindings.AddRange(
            CreateFinding(activeRun, IdentityFindingSeverity.Blocker),
            CreateFinding(activeRun, IdentityFindingSeverity.Warning),
            CreateFinding(invalidatedRun, IdentityFindingSeverity.Blocker));
        dbContext.CurrentResultReviews.Add(new CurrentResultReview
        {
            Id = Guid.NewGuid(),
            Status = CurrentResultReviewStatuses.Open,
            Source = "test",
            SourceGameId = "review-1"
        });
        await dbContext.SaveChangesAsync();

        var controller = new AdminController(dbContext);
        var result = await controller.GetAttentionSummary(CancellationToken.None);
        var summary = Assert.IsType<AdminAttentionSummary>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(1, summary.OpenResultReviews);
        Assert.Equal(1, summary.OpenIdentityBlockers);
        Assert.Equal(1, summary.OpenIdentityWarnings);
        Assert.Equal(1, summary.PossibleDuplicateAliasGroups);
        Assert.Equal(1, summary.TeamsMissingCountry);
        Assert.Equal(1, summary.UnratedEligibleGames);
    }

    private static Team CreateTeam(string name, string countryCode) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        CountryCode = countryCode,
        IsActive = true
    };

    private static TeamAlias CreateAlias(Team team, string name, string sourceTeamId) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = team.Id,
        Team = team,
        Source = "test",
        SourceTeamId = sourceTeamId,
        AliasName = name
    };

    private static IdentityHealthCheckFinding CreateFinding(
        IdentityHealthCheckRun run,
        string severity) => new()
    {
        Id = Guid.NewGuid(),
        RunId = run.Id,
        Run = run,
        FindingType = IdentityFindingType.PossibleDuplicate,
        Severity = severity,
        Status = IdentityFindingStatus.Open
    };

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
