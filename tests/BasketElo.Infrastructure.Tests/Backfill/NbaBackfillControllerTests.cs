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
    public async Task RangeTriggerCanQueueNewestSeasonFirst()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);
        var request = CreateRangeRequest("1946-1947", "1948-1949", onlyMissing: false) with
        {
            NewestFirst = true
        };

        var result = await controller.TriggerLeagueRangeBackfill(request, CancellationToken.None);

        var response = Assert.IsType<QueueBackfillJobsResponse>(Assert.IsType<AcceptedResult>(result).Value);
        Assert.True(response.NewestFirst);
        var queuedSeasons = await dbContext.BackfillJobs
            .OrderBy(job => job.CreatedAtUtc)
            .Select(job => job.Season)
            .ToListAsync();
        Assert.Equal(["1948-1949", "1947-1948", "1946-1947"], queuedSeasons);
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
                Source = FiveThirtyEightBasketballDataProvider.Source,
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
        var nbaSegments = catalog.GetLeagues()
            .Where(league => league.DisplayName == "United States: NBA")
            .ToList();
        var historical = Assert.Single(nbaSegments, league =>
            league.Provider == FiveThirtyEightBasketballDataProvider.Source);
        var seasons = nbaSegments
            .SelectMany(catalog.GetSeasonsForLeague)
            .OrderBy(SeasonLabelNormalizer.ParseStartYear)
            .ToList();
        dbContext.BackfillJobs.Add(new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = historical.Provider,
            Country = historical.Country,
            LeagueName = historical.LeagueName,
            Season = seasons[0],
            Status = BackfillJobStatus.Pending
        });
        await dbContext.SaveChangesAsync();
        var controller = new BackfillController(dbContext, null!, catalog, null!);

        var result = await controller.TriggerLeagueBackfill(
            new TriggerLeagueBackfillRequest
            {
                DisplayName = "United States: NBA",
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
        Assert.All(jobs, job => Assert.Equal("NBA", job.LeagueName));
        Assert.Equal(62, jobs.Count(job => job.Provider == FiveThirtyEightBasketballDataProvider.Source));
        Assert.Equal(18, jobs.Count(job => job.Provider == ApiSportsBasketballDataProvider.Source));
    }

    [Fact]
    public async Task LogicalNbaRangeRoutesAcrossHistoricalAndRecentProviders()
    {
        await using var dbContext = CreateDbContext();
        var controller = CreateController(dbContext);

        var result = await controller.TriggerLeagueRangeBackfill(
            new TriggerLeagueRangeBackfillRequest
            {
                DisplayName = "United States: NBA",
                StartSeason = "2007-2008",
                EndSeason = "2008-2009",
                OnlyMissing = false,
                NewestFirst = true,
                DryRun = true,
                MaxRequests = 2
            },
            CancellationToken.None);

        var response = Assert.IsType<QueueBackfillJobsResponse>(Assert.IsType<AcceptedResult>(result).Value);
        Assert.Equal("multiple", response.Provider);
        Assert.Equal(2, response.QueuedJobs);
        var jobs = await dbContext.BackfillJobs.OrderBy(x => x.CreatedAtUtc).ToListAsync();
        Assert.Collection(
            jobs,
            recent =>
            {
                Assert.Equal(ApiSportsBasketballDataProvider.Source, recent.Provider);
                Assert.Equal("2008-2009", recent.Season);
            },
            historical =>
            {
                Assert.Equal(FiveThirtyEightBasketballDataProvider.Source, historical.Provider);
                Assert.Equal("2007-2008", historical.Season);
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
        new(dbContext, null!, new BackfillCatalog(), null!);

    private static TriggerLeagueRangeBackfillRequest CreateRangeRequest(
        string startSeason,
        string endSeason,
        bool onlyMissing) =>
        new()
        {
            Provider = FiveThirtyEightBasketballDataProvider.Source,
            Country = "United States",
            LeagueName = "NBA",
            StartSeason = startSeason,
            EndSeason = endSeason,
            OnlyMissing = onlyMissing,
            DryRun = true,
            MaxRequests = 2
        };
}
