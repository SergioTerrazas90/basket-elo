using BasketElo.Api.Controllers;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaBackfillControllerTests
{
    [Fact]
    public async Task RangeTriggerQueuesOnlyRequestedHistoricalSeasons()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.TriggerLeagueRangeBackfill(
            CreateRangeRequest("1946-1947", "1959-1960", onlyMissing: false),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<QueueBackfillJobsResponse>(accepted.Value);
        Assert.Equal(14, response.QueuedJobs);
        Assert.Equal(14, response.RequestedSeasons);
        var seasons = await dbContext.BackfillJobs.OrderBy(job => job.Season).Select(job => job.Season).ToListAsync();
        Assert.Equal("1946-1947", seasons[0]);
        Assert.Equal("1959-1960", seasons[^1]);
        Assert.Equal(14, seasons.Count);
    }

    [Fact]
    public async Task RangeTriggerSkipsPendingAndRunningDuplicatesOnRerun()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);
        var request = CreateRangeRequest("1946-1947", "1948-1949", onlyMissing: false);

        await controller.TriggerLeagueRangeBackfill(request, CancellationToken.None);
        var rerun = await controller.TriggerLeagueRangeBackfill(request, CancellationToken.None);

        var response = Assert.IsType<QueueBackfillJobsResponse>(Assert.IsType<AcceptedResult>(rerun).Value);
        Assert.Equal(0, response.QueuedJobs);
        Assert.Equal(3, response.SkippedActiveJobs);
        Assert.Equal(3, await dbContext.BackfillJobs.CountAsync());
    }

    [Fact]
    public async Task OnlyMissingSkipsSeasonThatAlreadyContainsProviderGames()
    {
        await using var dbContext = CreateDbContext();
        var competition = new Competition { Id = Guid.NewGuid(), Name = "NBA", Type = "league" };
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Competition = competition,
            Label = "1946-1947"
        };
        dbContext.AddRange(
            competition,
            season,
            new Game
            {
                Id = Guid.NewGuid(),
                Source = BasketballReferenceBasketballDataProvider.Source,
                SourceGameId = "existing-game",
                CompetitionId = competition.Id,
                Competition = competition,
                SeasonId = season.Id,
                Season = season,
                Status = "finished"
            });
        await dbContext.SaveChangesAsync();
        var controller = CreateController(dbContext);

        var result = await controller.TriggerLeagueRangeBackfill(
            CreateRangeRequest("1946-1947", "1947-1948", onlyMissing: true),
            CancellationToken.None);

        var response = Assert.IsType<QueueBackfillJobsResponse>(Assert.IsType<AcceptedResult>(result).Value);
        Assert.Equal(1, response.QueuedJobs);
        Assert.Equal(1, response.SkippedExistingSeasons);
        Assert.Equal("1947-1948", Assert.Single(await dbContext.BackfillJobs.ToListAsync()).Season);
    }

    [Theory]
    [InlineData("not-a-season", "1959-1960", false, false)]
    [InlineData("1960-1961", "1959-1960", false, false)]
    [InlineData("1945-1946", "1959-1960", false, false)]
    [InlineData("1946-1947", "1959-1960", true, true)]
    public async Task RangeTriggerRejectsInvalidRequests(
        string startSeason,
        string endSeason,
        bool onlyMissing,
        bool replaceExisting)
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);
        var request = CreateRangeRequest(startSeason, endSeason, onlyMissing) with
        {
            ReplaceExisting = replaceExisting
        };

        var result = await controller.TriggerLeagueRangeBackfill(request, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(dbContext.BackfillJobs);
    }

    [Fact]
    public async Task FullLeagueTriggerQueuesEveryCatalogSeasonWithoutPendingDuplicates()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var catalog = new BackfillCatalog();
        var nba = Assert.Single(catalog.GetLeagues(), league =>
            league.Provider == BasketballReferenceBasketballDataProvider.Source &&
            league.LeagueName == "NBA");
        var seasons = catalog.GetSeasonsForLeague(nba).ToList();
        dbContext.BackfillJobs.Add(new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = nba.Provider,
            Country = nba.Country,
            LeagueName = nba.LeagueName,
            Season = seasons[0],
            Status = BackfillJobStatus.Pending
        });
        await dbContext.SaveChangesAsync();
        var controller = new BackfillController(dbContext, null!, catalog);

        var result = await controller.TriggerLeagueBackfill(
            new TriggerLeagueBackfillRequest
            {
                Provider = nba.Provider,
                Country = nba.Country,
                LeagueName = nba.LeagueName,
                DryRun = true,
                MaxRequests = 0
            },
            CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var jobs = await dbContext.BackfillJobs
            .OrderBy(job => job.Season)
            .ToListAsync();
        Assert.Equal(seasons.Count, jobs.Count);
        Assert.Equal(seasons, jobs.Select(job => job.Season));
        Assert.All(jobs, job =>
        {
            Assert.Equal("NBA", job.LeagueName);
            Assert.Equal("United States", job.Country);
            Assert.Equal(BasketballReferenceBasketballDataProvider.Source, job.Provider);
        });
    }

    private static BasketEloDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BasketEloDbContext(options);
    }

    private static BackfillController CreateController(BasketEloDbContext dbContext) =>
        new(dbContext, null!, new BackfillCatalog());

    private static TriggerLeagueRangeBackfillRequest CreateRangeRequest(
        string startSeason,
        string endSeason,
        bool onlyMissing) =>
        new()
        {
            Provider = BasketballReferenceBasketballDataProvider.Source,
            Country = "United States",
            LeagueName = "NBA",
            StartSeason = startSeason,
            EndSeason = endSeason,
            OnlyMissing = onlyMissing,
            DryRun = true,
            MaxRequests = 2
        };
}
