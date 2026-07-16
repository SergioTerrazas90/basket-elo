using BasketElo.Domain.Backfill;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BasketElo.Infrastructure.Tests.Backfill;

public class NbaCoverageExpectationsTests
{
    [Fact]
    public async Task ExceptionalSeasonsPassWhileEmptyAndObviouslyPartialSeasonsAreHighSeverity()
    {
        await using var dbContext = CreateDbContext();
        var catalog = new SelectedSeasonCatalog(
            new ConfiguredBackfillLeague(
                BasketballReferenceBasketballDataProvider.Source,
                "United States",
                "NBA",
                "United States: NBA",
                "1946-1947"),
            ["1998-1999", "2011-2012", "2019-2020", "2022-2023", "2000-2001"]);
        var competition = await SeedCompetitionAsync(dbContext, "NBA", "USA");
        await SeedCompletedSeasonAsync(dbContext, competition, "1998-1999", 760, catalog.League);
        await SeedCompletedSeasonAsync(dbContext, competition, "2011-2012", 1_020, catalog.League);
        await SeedCompletedSeasonAsync(dbContext, competition, "2019-2020", 1_080, catalog.League);
        await SeedCompletedSeasonAsync(dbContext, competition, "2022-2023", 100, catalog.League);
        await SeedCompletedSeasonAsync(dbContext, competition, "2000-2001", 0, catalog.League);

        var response = await new BackfillCoverageService(dbContext, catalog)
            .GetCoverageAsync(catalog.League.Provider, catalog.League.Country, catalog.League.LeagueName, CancellationToken.None);

        Assert.False(Row(response, "1998-1999").NeedsInspection);
        Assert.False(Row(response, "2011-2012").NeedsInspection);
        Assert.False(Row(response, "2019-2020").NeedsInspection);

        var partial = Row(response, "2022-2023");
        Assert.True(partial.NeedsInspection);
        Assert.Equal("high", partial.InspectionSeverity);
        Assert.Contains(partial.InspectionReasons, reason =>
            reason.Contains("modern 30-team NBA era", StringComparison.Ordinal) &&
            reason.Contains("found 100", StringComparison.Ordinal));

        var empty = Row(response, "2000-2001");
        Assert.True(empty.NeedsInspection);
        Assert.Equal("high", empty.InspectionSeverity);
        Assert.Contains("Latest completed run returned 0 games.", empty.InspectionReasons);
    }

    [Fact]
    public async Task GenericLeagueStillUsesMedianHeuristic()
    {
        await using var dbContext = CreateDbContext();
        var catalog = new SelectedSeasonCatalog(
            new ConfiguredBackfillLeague("api-sports", "Spain", "ACB", "Spain: ACB", "2021-2022"),
            ["2021-2022", "2022-2023", "2023-2024"]);
        var competition = await SeedCompetitionAsync(dbContext, "ACB", "ES");
        await SeedCompletedSeasonAsync(dbContext, competition, "2021-2022", 100, catalog.League);
        await SeedCompletedSeasonAsync(dbContext, competition, "2022-2023", 100, catalog.League);
        await SeedCompletedSeasonAsync(dbContext, competition, "2023-2024", 20, catalog.League);

        var response = await new BackfillCoverageService(dbContext, catalog)
            .GetCoverageAsync("api-sports", "Spain", "ACB", CancellationToken.None);

        var low = Row(response, "2023-2024");
        Assert.True(low.NeedsInspection);
        Assert.Contains(low.InspectionReasons, reason => reason.Contains("vs median 100", StringComparison.Ordinal));
    }

    private static BasketEloDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BasketEloDbContext(options);
    }

    private static async Task<Competition> SeedCompetitionAsync(
        BasketEloDbContext dbContext,
        string name,
        string countryCode)
    {
        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = name,
            CountryCode = countryCode,
            Type = "domestic_first_division"
        };
        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync();
        return competition;
    }

    private static async Task SeedCompletedSeasonAsync(
        BasketEloDbContext dbContext,
        Competition competition,
        string label,
        int gameCount,
        ConfiguredBackfillLeague league)
    {
        var startYear = SeasonLabelNormalizer.ParseStartYear(label);
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = label,
            StartDateUtc = new DateTime(startYear, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(startYear + 1, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };
        dbContext.Seasons.Add(season);
        dbContext.BackfillJobs.Add(new BackfillJob
        {
            Id = Guid.NewGuid(),
            Provider = league.Provider,
            Country = league.Country,
            LeagueName = league.LeagueName,
            Season = label,
            Status = BackfillJobStatus.Completed,
            DryRun = false,
            FinishedAtUtc = DateTime.UtcNow
        });

        for (var index = 0; index < gameCount; index++)
        {
            dbContext.Games.Add(new Game
            {
                Id = Guid.NewGuid(),
                Source = league.Provider,
                SourceGameId = $"{label}-{index}",
                CompetitionId = competition.Id,
                SeasonId = season.Id,
                GameDateTimeUtc = season.StartDateUtc.AddDays(index % 300),
                HomeTeamId = Guid.NewGuid(),
                AwayTeamId = Guid.NewGuid(),
                HomeScore = 100,
                AwayScore = 90,
                Status = "finished"
            });
        }

        await dbContext.SaveChangesAsync();
    }

    private static BackfillCoverageRow Row(BackfillCoverageResponse response, string season) =>
        Assert.Single(response.Rows, row => row.Season == season);

    private sealed class SelectedSeasonCatalog(
        ConfiguredBackfillLeague league,
        IReadOnlyCollection<string> seasons) : IBackfillCatalog
    {
        public ConfiguredBackfillLeague League { get; } = league;

        public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => [League];

        public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague configuredLeague) => seasons;
    }
}
