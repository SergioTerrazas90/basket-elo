using BasketElo.Api.Controllers;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Teams;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Admin;

public sealed class AdminDuplicateAliasGroupsControllerTests
{
    [Fact]
    public async Task DuplicateAliasGroupsReturnsTheRecordsBehindTheSummaryCount()
    {
        await using var dbContext = CreateDbContext();
        var first = CreateTeam("Valencia Basket", "ES");
        var second = CreateTeam("Valencia B", "es");
        var unrelated = CreateTeam("Real Madrid", "ES");
        dbContext.AddRange(first, second, unrelated);
        dbContext.TeamAliases.AddRange(
            CreateAlias(first, " Valencia ", "api-sports", "101"),
            CreateAlias(second, "valencia", "flashscore", "202"),
            CreateAlias(unrelated, "Madrid", "api-sports", "303"));
        await dbContext.SaveChangesAsync();

        var controller = new AdminTeamsController(dbContext, null!);
        var result = await controller.GetDuplicateAliasGroups(CancellationToken.None);
        var response = Assert.IsType<DuplicateTeamAliasGroupsResponse>(
            Assert.IsType<OkObjectResult>(result.Result).Value);

        var group = Assert.Single(response.Groups);
        Assert.Equal(1, response.GroupCount);
        Assert.Equal("Valencia", group.AliasName, ignoreCase: true);
        Assert.Equal("ES", group.CountryCode);
        Assert.Equal(2, group.Teams.Count);
        Assert.Contains(group.Teams, x => x.TeamId == first.Id && x.Aliases.Single().SourceTeamId == "101");
        Assert.Contains(group.Teams, x => x.TeamId == second.Id && x.Aliases.Single().SourceTeamId == "202");

        var accepted = await controller.AcceptDuplicateAliasGroup(
            new AcceptDuplicateTeamAliasGroupRequest(
                group.AliasName,
                group.CountryCode,
                group.Teams.Select(x => x.TeamId).ToList(),
                "These are separate clubs."),
            CancellationToken.None);
        Assert.IsType<NoContentResult>(accepted);

        var refreshedResult = await controller.GetDuplicateAliasGroups(CancellationToken.None);
        var refreshed = Assert.IsType<DuplicateTeamAliasGroupsResponse>(
            Assert.IsType<OkObjectResult>(refreshedResult.Result).Value);
        Assert.Equal(0, refreshed.GroupCount);
        var decision = await dbContext.IdentityReviewDecisions.SingleAsync();
        Assert.Equal(TeamAliasCollisionReview.AcceptedAction, decision.ResolutionAction);
        Assert.Equal("These are separate clubs.", decision.Note);
    }

    private static Team CreateTeam(string name, string countryCode) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        CountryCode = countryCode,
        IsActive = true
    };

    private static TeamAlias CreateAlias(
        Team team,
        string aliasName,
        string source,
        string sourceTeamId) => new()
    {
        Id = Guid.NewGuid(),
        TeamId = team.Id,
        Team = team,
        AliasName = aliasName,
        Source = source,
        SourceTeamId = sourceTeamId
    };

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
