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
}
