using System.Text.Json;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class EloRebuildServiceTests
{
    [Fact]
    public async Task NbaPoolRebuildsAllNbaHistoryWithoutChangingEuropePool()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var nba = Competition("NBA", "USA");
        var acb = Competition("ACB", "ES");
        var nba1946 = Season(nba, "1946-1947");
        var nba2023 = Season(nba, "2023-2024");
        var acb2023 = Season(acb, "2023-2024");
        var knicks = Team("New York Knicks", "USA");
        var huskies = Team("Toronto Huskies", "CAN");
        var lakers = Team("Los Angeles Lakers", "USA");
        var celtics = Team("Boston Celtics", "USA");
        var madrid = Team("Real Madrid", "ES");
        var barcelona = Team("Barcelona", "ES");
        var historicalGame = Game(nba, nba1946, knicks, huskies, new DateTime(1946, 11, 1, 12, 0, 0, DateTimeKind.Utc), 68, 66, "nba-historical");
        var playoffGame = Game(nba, nba2023, lakers, celtics, new DateTime(2024, 4, 21, 12, 0, 0, DateTimeKind.Utc), 106, 110, "nba-playoff");
        playoffGame.SourceUrl = "https://www.basketball-reference.com/playoffs/NBA_2024_games.html";
        var acbGame = Game(acb, acb2023, madrid, barcelona, new DateTime(2024, 1, 5, 20, 0, 0, DateTimeKind.Utc), 90, 85, "acb-game");
        var run = new EloRebuildRun
        {
            Id = Guid.NewGuid(),
            EloPoolKey = EloPoolKeys.Nba,
            RulesetVersion = EloRulesetVersions.AdjustedV1,
            CompetitionName = string.Empty,
            Status = EloRebuildRunStatus.Running,
            QueuedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow
        };
        dbContext.AddRange(
            nba, acb, nba1946, nba2023, acb2023,
            knicks, huskies, lakers, celtics, madrid, barcelona,
            historicalGame, playoffGame, acbGame, run);
        dbContext.TeamRatings.AddRange(
            new TeamRating
            {
                TeamId = knicks.Id,
                Team = knicks,
                EloPoolKey = EloPoolKeys.Nba,
                RulesetVersion = run.RulesetVersion,
                Elo = 999m,
                GamesPlayed = 99
            },
            new TeamRating
            {
                TeamId = madrid.Id,
                Team = madrid,
                EloPoolKey = EloPoolKeys.EuropeClubs,
                RulesetVersion = run.RulesetVersion,
                Elo = 1666m,
                GamesPlayed = 12,
                LastGameId = acbGame.Id,
                LastGame = acbGame
            });
        dbContext.RatingHistories.AddRange(
            History(historicalGame, knicks, huskies, EloPoolKeys.Nba, run.RulesetVersion, 999m),
            History(acbGame, madrid, barcelona, EloPoolKeys.EuropeClubs, run.RulesetVersion, 1666m));
        await dbContext.SaveChangesAsync();
        var service = new EloRebuildService(
            dbContext,
            new TestNotificationPublisher(),
            NullLogger<EloRebuildService>.Instance);

        var result = await service.RebuildAsync(run.Id, CancellationToken.None);

        Assert.Equal(EloRebuildRunStatus.Completed, result.Status);
        Assert.Equal(EloPoolKeys.Nba, result.EloPoolKey);
        Assert.Equal(2, result.GamesProcessed);
        Assert.Equal(4, result.TeamsRated);
        var ratings = await dbContext.TeamRatings.Where(x => x.RulesetVersion == run.RulesetVersion).ToListAsync();
        Assert.Contains(ratings, x => x.TeamId == knicks.Id && x.GamesPlayed == 1 && x.Elo != 999m);
        Assert.Contains(ratings, x => x.TeamId == lakers.Id && x.GamesPlayed == 1);
        Assert.Contains(ratings, x => x.TeamId == madrid.Id && x.GamesPlayed == 12 && x.Elo == 1666m);
        Assert.DoesNotContain(ratings, x => x.TeamId == barcelona.Id);

        var histories = await dbContext.RatingHistories.Include(x => x.Game).ToListAsync();
        Assert.Equal(4, histories.Count(x => x.Game.CompetitionId == nba.Id));
        Assert.Single(histories, x => x.GameId == acbGame.Id);
        Assert.All(histories.Where(x => x.GameId == playoffGame.Id), x => Assert.Equal(1m, x.CompetitionWeight));
        using var notes = JsonDocument.Parse(run.Notes!);
        Assert.Equal(EloPoolKeys.Nba, notes.RootElement.GetProperty("poolKey").GetString());
        Assert.Contains("Playoff", notes.RootElement.GetProperty("playoffPolicy").GetString(), StringComparison.Ordinal);
    }

    private static Competition Competition(string name, string countryCode) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = "domestic_first_division",
        EloPoolKey = name == "NBA" ? EloPoolKeys.Nba : EloPoolKeys.EuropeClubs,
        CountryCode = countryCode,
        Tier = 1
    };

    private static Season Season(Competition competition, string label) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = competition.Id,
        Competition = competition,
        Label = label,
        StartDateUtc = new DateTime(int.Parse(label[..4]), 8, 1, 0, 0, 0, DateTimeKind.Utc),
        EndDateUtc = new DateTime(int.Parse(label[..4]) + 1, 7, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Team Team(string name, string countryCode) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        CountryCode = countryCode
    };

    private static Game Game(
        Competition competition,
        Season season,
        Team home,
        Team away,
        DateTime date,
        short homeScore,
        short awayScore,
        string sourceGameId) => new()
    {
        Id = Guid.NewGuid(),
        Source = competition.Name == "NBA" ? "basketball-reference" : "test",
        SourceGameId = sourceGameId,
        CompetitionId = competition.Id,
        Competition = competition,
        SeasonId = season.Id,
        Season = season,
        HomeTeamId = home.Id,
        HomeTeam = home,
        AwayTeamId = away.Id,
        AwayTeam = away,
        GameDateTimeUtc = date,
        HomeScore = homeScore,
        AwayScore = awayScore,
        Status = "finished"
    };

    private static RatingHistory History(
        Game game,
        Team team,
        Team opponent,
        string poolKey,
        string ruleset,
        decimal elo) => new()
    {
        Id = Guid.NewGuid(),
        GameId = game.Id,
        Game = game,
        TeamId = team.Id,
        Team = team,
        OpponentTeamId = opponent.Id,
        OpponentTeam = opponent,
        EloPoolKey = poolKey,
        RulesetVersion = ruleset,
        GameDateTimeUtc = game.GameDateTimeUtc,
        PreElo = elo,
        PostElo = elo,
        KFactorUsed = EloCalculator.KFactor,
        ExpectedScore = 0.5m,
        ActualScore = 1m
    };

    private sealed class TestNotificationPublisher : IEloRebuildNotificationPublisher
    {
        public Task PublishAsync(EloRebuildRunNotification notification, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
