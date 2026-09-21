using BasketElo.Api.Controllers;
using BasketElo.Domain.Competitions;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Games;
using BasketElo.Domain.Teams;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Admin;

public sealed class AdminManualDataControllerTests
{
    [Fact]
    public async Task CreateCompetitionUsesEnumeratedTypeAndEloPool()
    {
        await using var dbContext = CreateDbContext();
        var controller = new AdminCompetitionsController(dbContext);

        var result = await controller.CreateCompetition(
            new CreateCompetitionAdminRequest(
                "EuroLeague Super Cup",
                "INTERNATIONAL_CUP",
                null,
                "EUROPE-CLUBS",
                1,
                true,
                CompetitionSupportPolicies.Supported,
                HomeAdvantagePolicies.Neutral),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<CompetitionAdminDetail>(created.Value);
        Assert.Equal(CompetitionTypeCatalog.InternationalCup, response.Type);
        Assert.Equal(EloPoolKeys.EuropeClubs, response.EloPoolKey);
    }

    [Theory]
    [InlineData("free text", EloPoolKeys.EuropeClubs)]
    [InlineData(CompetitionTypeCatalog.InternationalCup, "free-text-pool")]
    public async Task CreateCompetitionRejectsValuesOutsideEnumeratedCatalogs(string type, string eloPool)
    {
        await using var dbContext = CreateDbContext();
        var controller = new AdminCompetitionsController(dbContext);

        var result = await controller.CreateCompetition(
            new CreateCompetitionAdminRequest(
                "Invalid Competition",
                type,
                null,
                eloPool,
                1,
                true,
                CompetitionSupportPolicies.Supported,
                HomeAdvantagePolicies.Automatic),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(dbContext.Competitions);
    }

    [Fact]
    public async Task CreateTeamCreatesStandaloneCanonicalTeam()
    {
        await using var dbContext = CreateDbContext();
        var controller = new AdminTeamsController(
            dbContext,
            new IdentityHealthCheckService(dbContext, null!));

        var result = await controller.CreateTeam(
            new CreateTeamAdminRequest("Valencia Basket", "es", true, "Created manually"),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        var response = Assert.IsType<TeamAdminDetail>(created.Value);
        Assert.Equal("Valencia Basket", response.CanonicalName);
        Assert.Equal("ES", response.CountryCode);
        Assert.Equal("Created manually", response.Description);
        Assert.Equal(1, await dbContext.Teams.CountAsync());
    }

    [Fact]
    public async Task CreateManualFinishedGameCreatesSeasonAndEligibleGame()
    {
        await using var dbContext = CreateDbContext();
        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "EuroLeague Super Cup",
            Type = "cup",
            EloPoolKey = "europe-clubs",
            IsActive = true
        };
        var home = CreateTeam("Olympiacos", "GR");
        var away = CreateTeam("Fenerbahce", "TR");
        dbContext.AddRange(competition, home, away);
        await dbContext.SaveChangesAsync();

        var controller = new AdminGamesController(dbContext);
        var localTipoff = new DateTime(2026, 9, 20, 20, 30, 0, DateTimeKind.Unspecified);
        var result = await controller.CreateManualGame(
            new CreateManualGameRequest(
                competition.Id,
                "2026-2027",
                localTipoff,
                "Europe/Madrid",
                home.Id,
                away.Id,
                88,
                81,
                "finished",
                "Final",
                "Super Cup Final",
                true),
            CancellationToken.None);

        var created = Assert.IsType<CreatedResult>(result.Result);
        var response = Assert.IsType<ManualGameCreatedResponse>(created.Value);
        Assert.True(response.EloEligible);
        Assert.Equal(new DateTime(2026, 9, 20, 18, 30, 0, DateTimeKind.Utc), response.GameDateTimeUtc);

        var season = await dbContext.Seasons.SingleAsync();
        Assert.Equal("2026-2027", season.Label);
        Assert.Equal(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), season.StartDateUtc);
        Assert.Equal(new DateTime(2027, 6, 30, 23, 59, 59, DateTimeKind.Utc), season.EndDateUtc);

        var game = await dbContext.Games.SingleAsync();
        Assert.Equal("manual", game.Source);
        Assert.StartsWith("manual-", game.SourceGameId);
        Assert.True(game.HasManualResultOverride);
        Assert.True(game.IsNeutralSite);
        Assert.Null(game.EloExclusionReason);
    }

    [Fact]
    public async Task CreateManualGameRejectsDuplicateFixture()
    {
        await using var dbContext = CreateDbContext();
        var competition = new Competition { Id = Guid.NewGuid(), Name = "Cup", Type = "cup" };
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = "2026",
            StartDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };
        var home = CreateTeam("Home", "ES");
        var away = CreateTeam("Away", "FR");
        dbContext.AddRange(competition, season, home, away);
        dbContext.Games.Add(new Game
        {
            Id = Guid.NewGuid(),
            Source = "manual",
            SourceGameId = "manual-existing",
            CompetitionId = competition.Id,
            SeasonId = season.Id,
            GameDateTimeUtc = new DateTime(2026, 9, 20, 18, 30, 0, DateTimeKind.Utc),
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            Status = "scheduled"
        });
        await dbContext.SaveChangesAsync();

        var controller = new AdminGamesController(dbContext);
        var result = await controller.CreateManualGame(
            new CreateManualGameRequest(
                competition.Id,
                "2026",
                new DateTime(2026, 9, 20, 18, 30, 0, DateTimeKind.Unspecified),
                "UTC",
                away.Id,
                home.Id,
                null,
                null,
                "scheduled"),
            CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(1, await dbContext.Games.CountAsync());
    }

    private static Team CreateTeam(string name, string countryCode) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        CountryCode = countryCode,
        IsActive = true
    };

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
