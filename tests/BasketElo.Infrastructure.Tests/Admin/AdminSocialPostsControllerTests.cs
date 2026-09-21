using BasketElo.Api.Controllers;
using BasketElo.Domain.Admin;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Admin;

public class AdminSocialPostsControllerTests
{
    [Fact]
    public async Task DailyPicksSelectTrueUpsetDistinctMoverAndCurrentRanking()
    {
        await using var dbContext = CreateDbContext();
        var contentDate = NextDateForRankingTarget(0);
        var competition = CreateCompetition("NBA", EloPoolKeys.Nba);
        var season = CreateSeason(competition, "2026-2027");
        var underdog = CreateTeam("Underdog", "USA");
        var favorite = CreateTeam("Favorite", "USA");
        var mover = CreateTeam("Big Mover", "USA");
        var opponent = CreateTeam("Other Team", "USA");
        var upsetGame = CreateGame(competition, season, underdog, favorite, contentDate, 108, 101, "upset");
        var moverGame = CreateGame(competition, season, mover, opponent, contentDate, 119, 103, "mover");

        dbContext.AddRange(competition, season, underdog, favorite, mover, opponent, upsetGame, moverGame);
        AddRating(dbContext, underdog, 1518m);
        AddRating(dbContext, favorite, 1700m);
        AddRating(dbContext, mover, 1655m);
        AddRating(dbContext, opponent, 1490m);
        AddGameHistory(dbContext, upsetGame, underdog, favorite, 1500m, 1518m, 0.20m, 18m);
        AddGameHistory(dbContext, moverGame, mover, opponent, 1630m, 1655m, 0.70m, 25m);
        await dbContext.SaveChangesAsync();

        var controller = new AdminController(dbContext);
        var result = await controller.GetSocialPosts(contentDate, CancellationToken.None);
        var response = Assert.IsType<AdminSocialPostsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.False(response.UsedFallbackDate);
        Assert.Equal(contentDate, response.ContentDate);
        Assert.Equal(2, response.RatedGames);
        Assert.Equal(3, response.Posts.Count);

        var upset = Assert.Single(response.Posts, x => x.Type == "upset");
        Assert.Equal("Underdog", upset.Game!.Winner);
        Assert.Equal(20m, upset.Game.ExpectedWinPercentage);
        Assert.Contains("20% chance", upset.CaptionEnglish);

        var biggestMove = Assert.Single(response.Posts, x => x.Type == "mover");
        Assert.Equal("Big Mover", biggestMove.Game!.Winner);
        Assert.Equal(25m, biggestMove.Game.EloDelta);

        var ranking = Assert.Single(response.Posts, x => x.Type == "ranking");
        Assert.Equal("NBA", ranking.League);
        Assert.Equal(4, ranking.Rankings.Count);
        Assert.Equal("Favorite", ranking.Rankings.First().TeamName);
    }

    [Fact]
    public async Task DailyPicksFallBackToLatestRatedGameDate()
    {
        await using var dbContext = CreateDbContext();
        var contentDate = new DateOnly(2026, 6, 18);
        var requestedDate = contentDate.AddDays(10);
        var competition = CreateCompetition("NBA", EloPoolKeys.Nba);
        var season = CreateSeason(competition, "2025-2026");
        var winner = CreateTeam("Winner", "USA");
        var loser = CreateTeam("Loser", "USA");
        var game = CreateGame(competition, season, winner, loser, contentDate, 100, 90, "latest");

        dbContext.AddRange(competition, season, winner, loser, game);
        AddRating(dbContext, winner, 1600m);
        AddRating(dbContext, loser, 1500m);
        AddGameHistory(dbContext, game, winner, loser, 1580m, 1600m, 0.45m, 20m);
        await dbContext.SaveChangesAsync();

        var controller = new AdminController(dbContext);
        var result = await controller.GetSocialPosts(requestedDate, CancellationToken.None);
        var response = Assert.IsType<AdminSocialPostsResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.True(response.UsedFallbackDate);
        Assert.Equal(requestedDate, response.RequestedDate);
        Assert.Equal(contentDate, response.ContentDate);
        Assert.Equal(1, response.RatedGames);
        Assert.Contains(response.Posts, x => x.Type == "upset");
    }

    private static DateOnly NextDateForRankingTarget(int targetIndex)
    {
        var date = new DateOnly(2026, 10, 1);
        while (Math.Abs(date.DayNumber % 3) != targetIndex)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static Competition CreateCompetition(string name, string poolKey) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = "league",
        CountryCode = "USA",
        EloPoolKey = poolKey,
        Tier = 1
    };

    private static Season CreateSeason(Competition competition, string label) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = competition.Id,
        Competition = competition,
        Label = label,
        StartDateUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        EndDateUtc = new DateTime(2027, 7, 31, 23, 59, 59, DateTimeKind.Utc)
    };

    private static Team CreateTeam(string name, string countryCode) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        CountryCode = countryCode,
        IsActive = true
    };

    private static Game CreateGame(
        Competition competition,
        Season season,
        Team home,
        Team away,
        DateOnly date,
        short homeScore,
        short awayScore,
        string sourceId) => new()
        {
            Id = Guid.NewGuid(),
            Source = "test",
            SourceGameId = sourceId,
            CompetitionId = competition.Id,
            Competition = competition,
            SeasonId = season.Id,
            Season = season,
            GameDateTimeUtc = DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(19, 0)), DateTimeKind.Utc),
            HomeTeamId = home.Id,
            HomeTeam = home,
            AwayTeamId = away.Id,
            AwayTeam = away,
            HomeScore = homeScore,
            AwayScore = awayScore,
            Status = "finished",
            EloEligible = true
        };

    private static void AddRating(BasketEloDbContext dbContext, Team team, decimal elo)
    {
        dbContext.TeamRatings.Add(new TeamRating
        {
            TeamId = team.Id,
            Team = team,
            EloPoolKey = EloPoolKeys.Nba,
            RulesetVersion = EloRulesetVersions.Default,
            Elo = elo,
            GamesPlayed = 10
        });
    }

    private static void AddGameHistory(
        BasketEloDbContext dbContext,
        Game game,
        Team winner,
        Team loser,
        decimal winnerPreElo,
        decimal winnerPostElo,
        decimal winnerExpectedScore,
        decimal winnerDelta)
    {
        dbContext.RatingHistories.AddRange(
            new RatingHistory
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                Game = game,
                TeamId = winner.Id,
                Team = winner,
                OpponentTeamId = loser.Id,
                OpponentTeam = loser,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.Default,
                GameDateTimeUtc = game.GameDateTimeUtc,
                PreElo = winnerPreElo,
                PostElo = winnerPostElo,
                EloDelta = winnerDelta,
                ExpectedScore = winnerExpectedScore,
                ActualScore = 1m,
                KFactorUsed = 20
            },
            new RatingHistory
            {
                Id = Guid.NewGuid(),
                GameId = game.Id,
                Game = game,
                TeamId = loser.Id,
                Team = loser,
                OpponentTeamId = winner.Id,
                OpponentTeam = winner,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = EloRulesetVersions.Default,
                GameDateTimeUtc = game.GameDateTimeUtc,
                PreElo = 1600m,
                PostElo = 1600m - winnerDelta,
                EloDelta = -winnerDelta,
                ExpectedScore = 1m - winnerExpectedScore,
                ActualScore = 0m,
                KFactorUsed = 20
            });
    }

    private static BasketEloDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
