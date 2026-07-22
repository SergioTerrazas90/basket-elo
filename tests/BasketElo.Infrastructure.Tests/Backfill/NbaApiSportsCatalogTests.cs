using System.Text.Json;
using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaApiSportsCatalogTests
{
    [Theory]
    [InlineData("132", "Atlanta Hawks")]
    [InlineData("135", "Charlotte Hornets")]
    [InlineData("150", "New Orleans Pelicans")]
    [InlineData("152", "Oklahoma City Thunder")]
    [InlineData("161", "Washington Wizards")]
    public void ResolvesReviewedFranchiseIds(string sourceTeamId, string expectedName)
    {
        Assert.Equal(expectedName, NbaApiSportsCatalog.GetCanonicalName(sourceTeamId));
    }

    [Fact]
    public void ExcludesPreseasonGames()
    {
        var reason = NbaApiSportsCatalog.GetExclusionReason(
            NbaApiSportsCatalog.LeagueId,
            "2025-2026",
            new DateTime(2025, 10, 2, 16, 0, 0, DateTimeKind.Utc),
            "151",
            "154");

        Assert.Equal("nba-preseason", reason);
    }

    [Fact]
    public void IncludesGamesFromRegularSeasonOpener()
    {
        var reason = NbaApiSportsCatalog.GetExclusionReason(
            NbaApiSportsCatalog.LeagueId,
            "2011-2012",
            new DateTime(2011, 12, 25, 17, 0, 0, DateTimeKind.Utc),
            "133",
            "151");

        Assert.Null(reason);
    }

    [Fact]
    public void ExcludesNonFranchiseExhibitionTeams()
    {
        var reason = NbaApiSportsCatalog.GetExclusionReason(
            NbaApiSportsCatalog.LeagueId,
            "2008-2009",
            new DateTime(2009, 2, 15, 17, 0, 0, DateTimeKind.Utc),
            "1416",
            "1417");

        Assert.Equal("nba-non-franchise-exhibition", reason);
    }

    [Fact]
    public void RejectsUnreviewedNbaSeasonsButDoesNotAffectOtherLeagues()
    {
        var nbaReason = NbaApiSportsCatalog.GetExclusionReason(
            NbaApiSportsCatalog.LeagueId,
            "2026-2027",
            new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc),
            "133",
            "151");
        var otherLeagueReason = NbaApiSportsCatalog.GetExclusionReason(
            "99",
            "2026-2027",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "1",
            "2");

        Assert.Equal("nba-unreviewed-season", nbaReason);
        Assert.Null(otherLeagueReason);
    }

    [Fact]
    public async Task ProcessorPersistsOnlyEligibleGamesWithCanonicalIdentity()
    {
        await using var dbContext = new BasketEloDbContext(
            new DbContextOptionsBuilder<BasketEloDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        var catalog = new BackfillCatalog();
        var processor = new BackfillJobProcessor(
            dbContext,
            [new ApiSportsNbaFixtureProvider()],
            new IdentityHealthCheckService(dbContext, catalog),
            catalog,
            NullLogger<BackfillJobProcessor>.Instance);
        var job = new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = ApiSportsBasketballDataProvider.Source,
            Country = "USA",
            LeagueName = "NBA",
            Season = "2025-2026",
            DryRun = false,
            MaxRequests = 2,
            Status = BackfillJobStatus.Pending
        };
        dbContext.BackfillJobs.Add(job);
        await dbContext.SaveChangesAsync();

        Assert.True(await processor.TryProcessNextPendingJobAsync(CancellationToken.None));

        Assert.Equal(BackfillJobStatus.Completed, job.Status);
        Assert.Equal("USA", (await dbContext.Competitions.SingleAsync()).CountryCode);
        var persistedGames = await dbContext.Games.ToListAsync();
        Assert.True(
            persistedGames.Count == 2,
            string.Join(",", persistedGames.Select(game => $"{game.SourceGameId}:{game.EloEligible}")));
        Assert.Contains(persistedGames, game => game.EloEligible);
        var exhibition = Assert.Single(persistedGames, game => !game.EloEligible);
        Assert.Equal("nba-non-franchise-exhibition", exhibition.EloExclusionReason);
        Assert.Equal(
            ["Boston Celtics", "New York Knicks", "Team Stars", "Team World"],
            await dbContext.Teams.OrderBy(team => team.CanonicalName).Select(team => team.CanonicalName).ToListAsync());
        using var summary = JsonDocument.Parse(job.SummaryJson!);
        Assert.Equal(2, summary.RootElement.GetProperty("GamesFetched").GetInt32());
        Assert.Equal(1, summary.RootElement.GetProperty("GamesFiltered").GetInt32());
        Assert.Equal(
            "nba-non-franchise-exhibition:1",
            summary.RootElement.GetProperty("FilteredGameReasons")[0].GetString());
    }

    private sealed class ApiSportsNbaFixtureProvider : IBasketballDataProvider
    {
        public string SourceKey => ApiSportsBasketballDataProvider.Source;

        public Task<BasketballProviderLeague?> ResolveLeagueAsync(
            string country,
            string leagueName,
            BackfillExecutionContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult<BasketballProviderLeague?>(new(SourceKey, NbaApiSportsCatalog.LeagueId, "NBA", "US"));

        public Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
            BasketballProviderLeague league,
            string season,
            BackfillExecutionContext context,
            CancellationToken cancellationToken)
        {
            IReadOnlyCollection<BasketballProviderGame> games =
            [
                new(
                    SourceKey,
                    "eligible",
                    new DateTime(2025, 10, 21, 23, 0, 0, DateTimeKind.Utc),
                    "game finished",
                    "133",
                    "Boston Celtics",
                    "151",
                    "New York Knicks",
                    101,
                    99),
                new(
                    SourceKey,
                    "exhibition",
                    new DateTime(2026, 2, 15, 23, 0, 0, DateTimeKind.Utc),
                    "game finished",
                    "0",
                    "Team Stars",
                    "1414",
                    "Team World",
                    120,
                    118,
                    ExclusionReason: "nba-non-franchise-exhibition")
            ];
            return Task.FromResult((games, false, (IReadOnlyCollection<string>)[]));
        }
    }
}
