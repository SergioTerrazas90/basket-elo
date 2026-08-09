using BasketElo.Api.Controllers;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Games;

public class GamesControllerTournamentCycleTests
{
    [Fact]
    public async Task SeasonDateAndTournamentCycleFiltersRemainIndependent()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Home", CountryCode = "ES" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Away", CountryCode = "FR" };
        var cycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "eurobasket-2021",
            Family = "EuroBasket",
            EditionLabel = "2021",
            DisplayName = "EuroBasket 2021"
        };
        var qualifierCompetition = new Competition { Id = Guid.NewGuid(), Name = "EuroBasket Qualifiers", CountryCode = null };
        var finalsCompetition = new Competition { Id = Guid.NewGuid(), Name = "EuroBasket", CountryCode = null };
        var clubCompetition = new Competition { Id = Guid.NewGuid(), Name = "Example Club League", CountryCode = "ES" };
        var qualifierSeason = new Season { Id = Guid.NewGuid(), CompetitionId = qualifierCompetition.Id, Competition = qualifierCompetition, Label = "2021" };
        var finalsSeason = new Season { Id = Guid.NewGuid(), CompetitionId = finalsCompetition.Id, Competition = finalsCompetition, Label = "2021" };
        var clubSeason = new Season { Id = Guid.NewGuid(), CompetitionId = clubCompetition.Id, Competition = clubCompetition, Label = "2018" };

        dbContext.AddRange(home, away, cycle, qualifierCompetition, finalsCompetition, clubCompetition, qualifierSeason, finalsSeason, clubSeason);
        dbContext.Games.AddRange(
            CreateGame("qualifier", qualifierCompetition, qualifierSeason, cycle, home, away, new DateTime(2018, 11, 29, 12, 0, 0, DateTimeKind.Utc)),
            CreateGame("final", finalsCompetition, finalsSeason, cycle, home, away, new DateTime(2021, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
            CreateGame("club", clubCompetition, clubSeason, null, home, away, new DateTime(2018, 3, 1, 12, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var controller = new GamesController(dbContext);

        var strictSeason = await controller.GetGames(
            null, null, null, "2018", null, null, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var seasonResponse = Assert.IsType<OkObjectResult>(strictSeason.Result).Value as BasketElo.Domain.Games.GameBrowseResponse;
        Assert.NotNull(seasonResponse);
        Assert.Single(seasonResponse!.Games);
        Assert.Equal("club", seasonResponse.Games.Single().SourceGameId);

        var cycleHistory = await controller.GetGames(
            null, null, null, null, "eurobasket-2021", null, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var cycleResponse = Assert.IsType<OkObjectResult>(cycleHistory.Result).Value as BasketElo.Domain.Games.GameBrowseResponse;
        Assert.NotNull(cycleResponse);
        Assert.Equal(2, cycleResponse!.Games.Count);
        Assert.All(cycleResponse.Games, game => Assert.Equal("EuroBasket 2021", game.TournamentCycle));

        var playedYear = await controller.GetGames(
            null, null, null, null, null, 2018, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var playedYearResponse = Assert.IsType<OkObjectResult>(playedYear.Result).Value as BasketElo.Domain.Games.GameBrowseResponse;
        Assert.NotNull(playedYearResponse);
        Assert.Equal(2, playedYearResponse!.Games.Count);
    }

    [Fact]
    public async Task AmeriCupStagesShareTheTournamentCycleFilter()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Home", CountryCode = "US" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Away", CountryCode = "CA" };
        var cycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "americup-2025",
            Family = "FIBA AmeriCup",
            EditionLabel = "2025",
            DisplayName = "FIBA AmeriCup 2025"
        };

        var stages = new[]
        {
            new Competition { Id = Guid.NewGuid(), Name = "FIBA AmeriCup", CountryCode = "AME" },
            new Competition { Id = Guid.NewGuid(), Name = "FIBA AmeriCup Qualifiers", CountryCode = "AME" },
            new Competition { Id = Guid.NewGuid(), Name = "FIBA AmeriCup Pre-Qualifiers", CountryCode = "AME" }
        };
        var seasons = stages.Select(stage => new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = stage.Id,
            Competition = stage,
            Label = "2025"
        }).ToArray();

        dbContext.AddRange(home, away, cycle);
        dbContext.AddRange(stages);
        dbContext.AddRange(seasons);
        dbContext.Games.AddRange(stages.Select((stage, index) => CreateGame(
            $"americup-{index}",
            stage,
            seasons[index],
            cycle,
            home,
            away,
            new DateTime(2025, 8, 20 + index, 12, 0, 0, DateTimeKind.Utc))));
        await dbContext.SaveChangesAsync();

        var controller = new GamesController(dbContext);
        var result = await controller.GetGames(
            null, null, null, null, "americup-2025", null, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(result.Result).Value as BasketElo.Domain.Games.GameBrowseResponse;

        Assert.NotNull(response);
        Assert.Equal(3, response!.Games.Count);
        Assert.All(response.Games, game => Assert.Equal("FIBA AmeriCup 2025", game.TournamentCycle));
    }

    [Fact]
    public async Task HistoricalWorldCupQualificationLinkFiltersWithoutDuplicatingTheGame()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Home", CountryCode = "ES" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Away", CountryCode = "FR" };
        var originalCycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "eurobasket-2009",
            Family = "EuroBasket",
            EditionLabel = "2009",
            DisplayName = "EuroBasket 2009"
        };
        var worldCupCycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "worldcup-2010",
            Family = "FIBA Basketball World Cup",
            EditionLabel = "2010",
            DisplayName = "FIBA Basketball World Cup 2010"
        };
        var competition = new Competition { Id = Guid.NewGuid(), Name = "EuroBasket", CountryCode = "EUR" };
        var season = new Season { Id = Guid.NewGuid(), CompetitionId = competition.Id, Competition = competition, Label = "2009" };
        var game = CreateGame("historical-2010-route", competition, season, originalCycle, home, away, new DateTime(2009, 9, 10, 12, 0, 0, DateTimeKind.Utc));
        game.TournamentCycleLinks.Add(new GameTournamentCycleLink
        {
            GameId = game.Id,
            TournamentCycleId = worldCupCycle.Id,
            Game = game,
            TournamentCycle = worldCupCycle,
            Stage = "qualifier",
            Source = "historical-world-cup-qualification"
        });

        dbContext.AddRange(home, away, originalCycle, worldCupCycle, competition, season, game);
        await dbContext.SaveChangesAsync();

        var controller = new GamesController(dbContext);
        var result = await controller.GetGames(
            null, null, null, null, "worldcup-2010", null, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var response = Assert.IsType<OkObjectResult>(result.Result).Value as BasketElo.Domain.Games.GameBrowseResponse;

        Assert.NotNull(response);
        var row = Assert.Single(response!.Games);
        Assert.Equal("historical-2010-route", row.SourceGameId);
        Assert.Equal("FIBA Basketball World Cup 2010", row.TournamentCycle);

        var qualifierAliasResult = await controller.GetGames(
            null, null, "FIBA Basketball World Cup Qualifiers", null, null, null, null, null, null, null, null, null, null, 1, 50, CancellationToken.None);
        var qualifierAliasResponse = Assert.IsType<OkObjectResult>(qualifierAliasResult.Result).Value as BasketElo.Domain.Games.GameBrowseResponse;
        Assert.NotNull(qualifierAliasResponse);
        Assert.Contains(qualifierAliasResponse!.Games, item => item.SourceGameId == "historical-2010-route");
    }

    private static Game CreateGame(
        string sourceGameId,
        Competition competition,
        Season season,
        TournamentCycle? cycle,
        Team home,
        Team away,
        DateTime dateUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            Source = "test",
            SourceGameId = sourceGameId,
            CompetitionId = competition.Id,
            Competition = competition,
            SeasonId = season.Id,
            Season = season,
            TournamentCycleId = cycle?.Id,
            TournamentCycle = cycle,
            HomeTeamId = home.Id,
            HomeTeam = home,
            AwayTeamId = away.Id,
            AwayTeam = away,
            GameDateTimeUtc = dateUtc,
            Status = "finished",
            HomeScore = 80,
            AwayScore = 70
        };
}
