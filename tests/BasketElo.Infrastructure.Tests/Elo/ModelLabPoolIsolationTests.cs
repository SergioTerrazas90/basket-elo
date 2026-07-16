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
    public async Task AllCompetitionsRejectsMixedRatingPools()
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
            ModelLabScopeTypes.AllCompetitions);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RunAsync(request, CancellationToken.None));

        Assert.Contains("cannot mix", exception.Message, StringComparison.OrdinalIgnoreCase);
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
