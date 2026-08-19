using BasketElo.Api.Controllers;
using BasketElo.Domain.Entities;
using BasketElo.Domain.Games;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Games;

public class GamesControllerReviewTests
{
    [Fact]
    public async Task NeedsReviewExcludesTerminalNonPlayedGames()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var competition = new Competition { Id = Guid.NewGuid(), Name = "League", CountryCode = "IL" };
        var season = new Season { Id = Guid.NewGuid(), CompetitionId = competition.Id, Competition = competition, Label = "2025-26" };
        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Home", CountryCode = "IL" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Away", CountryCode = "IL" };
        dbContext.AddRange(competition, season, home, away);

        var oldDate = DateTime.UtcNow.AddDays(-30);
        dbContext.Games.AddRange(
            CreateGame("cancelled", "cancelled", false, oldDate, competition, season, home, away),
            CreateGame("game-cancelled", "Game Cancelled", false, oldDate, competition, season, home, away),
            CreateGame("postponed", "game postponed", false, oldDate, competition, season, home, away),
            CreateGame("abandoned", "abandoned", false, oldDate, competition, season, home, away),
            CreateGame("stale", "not started", false, oldDate, competition, season, home, away),
            CreateGame("score-pending", "score_pending", false, oldDate, competition, season, home, away),
            CreateGame("missing-score", "game finished", false, oldDate, competition, season, home, away));
        await dbContext.SaveChangesAsync();

        var controller = new GamesController(dbContext);
        var result = await controller.GetGames(
            null, null, null, null, null, null, null, null, null, null, "needs_review", null, null, 1, 50, CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(result.Result).Value as GameBrowseResponse;

        Assert.NotNull(response);
        Assert.Equal(3, response!.Games.Count);
        Assert.Equal(new[] { "missing-score", "score-pending", "stale" }, response.Games.Select(x => x.SourceGameId).OrderBy(x => x));
        Assert.All(response.Games, game => Assert.True(game.NeedsReview));
    }

    private static Game CreateGame(
        string sourceGameId,
        string status,
        bool eloEligible,
        DateTime dateUtc,
        Competition competition,
        Season season,
        Team home,
        Team away)
        => new()
        {
            Id = Guid.NewGuid(),
            Source = "test",
            SourceGameId = sourceGameId,
            CompetitionId = competition.Id,
            Competition = competition,
            SeasonId = season.Id,
            Season = season,
            HomeTeamId = home.Id,
            HomeTeam = home,
            AwayTeamId = away.Id,
            AwayTeam = away,
            GameDateTimeUtc = dateUtc,
            Status = status,
            EloEligible = eloEligible,
            EloExclusionReason = eloEligible ? null : "manual_result_not_eligible"
        };
}
