using BasketElo.Api.Controllers;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Games;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Games;

public sealed class GamesControllerCountryFilterTests
{
    [Fact]
    public async Task GetGames_FiltersSerbiaDisplayNameAgainstStoredCountryCode()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var serbianCompetition = new Competition { Id = Guid.NewGuid(), Name = "First League", CountryCode = "RS" };
        var spanishCompetition = new Competition { Id = Guid.NewGuid(), Name = "ACB", CountryCode = "ES" };
        var serbianSeason = new Season { Id = Guid.NewGuid(), Competition = serbianCompetition, CompetitionId = serbianCompetition.Id, Label = "2025-26" };
        var spanishSeason = new Season { Id = Guid.NewGuid(), Competition = spanishCompetition, CompetitionId = spanishCompetition.Id, Label = "2025-26" };
        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Home", CountryCode = "RS" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Away", CountryCode = "RS" };

        dbContext.AddRange(serbianCompetition, spanishCompetition, serbianSeason, spanishSeason, home, away);
        dbContext.Games.AddRange(
            CreateGame("serbian-game", serbianCompetition, serbianSeason, home, away),
            CreateGame("spanish-game", spanishCompetition, spanishSeason, home, away));
        await dbContext.SaveChangesAsync();

        var controller = new GamesController(dbContext);
        var result = await controller.GetGames(
            "Serbia", null, null, null, null, null, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(result.Result).Value as GameBrowseResponse;

        Assert.NotNull(response);
        var game = Assert.Single(response!.Games);
        Assert.Equal("serbian-game", game.SourceGameId);
        Assert.Equal("Serbia", game.Country);
    }

    private static Game CreateGame(
        string sourceGameId,
        Competition competition,
        Season season,
        Team home,
        Team away)
        => new()
        {
            Id = Guid.NewGuid(),
            Source = "test",
            SourceGameId = sourceGameId,
            Competition = competition,
            CompetitionId = competition.Id,
            Season = season,
            SeasonId = season.Id,
            HomeTeam = home,
            HomeTeamId = home.Id,
            AwayTeam = away,
            AwayTeamId = away.Id,
            GameDateTimeUtc = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Status = "finished",
            HomeScore = 80,
            AwayScore = 70,
            EloEligible = true
        };
}
