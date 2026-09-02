using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Elo;

public class ModelLabPoolIsolationTests
{
    [Fact]
    public async Task AllCompetitionsRunsOnlyInsideRequestedPool()
    {
        await using var dbContext = new BasketEloDbContext(
            new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var nba = Competition("NBA", "USA", EloPoolKeys.Nba);
        var acb = Competition("ACB", "ES", EloPoolKeys.EuropeClubs);
        var home = Team("Home");
        var away = Team("Away");
        var nbaSeason = Season(nba);
        var acbSeason = Season(acb);
        dbContext.AddRange(
            nba,
            acb,
            home,
            away,
            nbaSeason,
            acbSeason,
            Game(nba, nbaSeason, home, away, "nba-1"),
            Game(acb, acbSeason, home, away, "acb-1"));
        await dbContext.SaveChangesAsync();

        var service = new ModelLabBacktestService(dbContext);
        var request = new ModelLabBacktestRequest(
            "Mixed pool test",
            new ModelLabParameterSet(1500m, 20, 100m, 400m, true, 20m, 1m),
            "All competitions",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ModelLabScopeTypes.AllCompetitions,
            EloPoolKey: EloPoolKeys.Nba);

        var execution = await service.RunDetailedAsync(request, CancellationToken.None);
        var result = execution.Response;

        Assert.Equal(EloPoolKeys.Nba, result.EloPoolKey);
        Assert.Equal(1, result.Summary.ScoredGames);
        Assert.StartsWith("All NBA", result.LeagueName);
        Assert.Equal(2, execution.Evolution.Count);
        Assert.All(execution.Evolution, point => Assert.Equal("NBA", point.CompetitionName));
    }

    [Fact]
    public async Task SelectedCompetitionsRejectsCompetitionOutsideRequestedPool()
    {
        await using var dbContext = new BasketEloDbContext(
            new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var nba = Competition("NBA", "USA", EloPoolKeys.Nba);
        var acb = Competition("ACB", "ES", EloPoolKeys.EuropeClubs);
        var home = Team("Home");
        var away = Team("Away");
        var nbaSeason = Season(nba);
        var acbSeason = Season(acb);
        dbContext.AddRange(
            nba,
            acb,
            home,
            away,
            nbaSeason,
            acbSeason,
            Game(nba, nbaSeason, home, away, "nba-1"),
            Game(acb, acbSeason, home, away, "acb-1"));
        await dbContext.SaveChangesAsync();

        var service = new ModelLabBacktestService(dbContext);
        var request = new ModelLabBacktestRequest(
            "Mixed pool test",
            new ModelLabParameterSet(1500m, 20, 100m, 400m, true, 20m, 1m),
            "2 competitions",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2024, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            ModelLabScopeTypes.SelectedCompetitions,
            [nba.Id, acb.Id],
            EloPoolKeys.Nba);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RunAsync(request, CancellationToken.None));

        Assert.Contains("cannot mix", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OptionsExposeCompetitionPoolKeysAndNormalizeSeasonLabels()
    {
        await using var dbContext = new BasketEloDbContext(
            new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var nba = Competition("NBA", "USA", EloPoolKeys.Nba);
        var home = Team("Home");
        var away = Team("Away");
        var season = Season(nba);
        var legacySeason = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = nba.Id,
            Competition = nba,
            Label = "2024",
            StartDateUtc = season.StartDateUtc,
            EndDateUtc = season.EndDateUtc
        };
        dbContext.AddRange(
            nba,
            home,
            away,
            season,
            legacySeason,
            Game(nba, season, home, away, "nba-1"),
            Game(nba, legacySeason, home, away, "nba-2"));
        await dbContext.SaveChangesAsync();

        var options = await new ModelLabBacktestService(dbContext).GetOptionsAsync(CancellationToken.None);

        var pool = Assert.Single(options.Pools);
        Assert.Equal(EloPoolKeys.Nba, pool.Key);
        Assert.Equal(EloPoolKeys.Nba, Assert.Single(options.Competitions).EloPoolKey);
        var modelLabSeason = Assert.Single(options.Seasons);
        Assert.Equal("2024-2025", modelLabSeason.Label);
    }

    private static Competition Competition(string name, string country, string pool) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Type = "league",
        CountryCode = country,
        EloPoolKey = pool
    };

    private static Team Team(string name) => new()
    {
        Id = Guid.NewGuid(),
        CanonicalName = name,
        CountryCode = "USA"
    };

    private static Season Season(Competition competition) => new()
    {
        Id = Guid.NewGuid(),
        CompetitionId = competition.Id,
        Competition = competition,
        Label = "2024-2025",
        StartDateUtc = new DateTime(2024, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        EndDateUtc = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Game Game(Competition competition, Season season, Team home, Team away, string sourceId) => new()
    {
        Id = Guid.NewGuid(),
        Source = "test",
        SourceGameId = sourceId,
        CompetitionId = competition.Id,
        Competition = competition,
        SeasonId = season.Id,
        Season = season,
        HomeTeamId = home.Id,
        HomeTeam = home,
        AwayTeamId = away.Id,
        AwayTeam = away,
        GameDateTimeUtc = new DateTime(2024, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        HomeScore = 90,
        AwayScore = 80,
        Status = "finished"
    };
}
