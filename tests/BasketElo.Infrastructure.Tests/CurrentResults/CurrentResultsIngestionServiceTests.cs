using BasketElo.Domain.Backfill;
using BasketElo.Domain.CurrentResults;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Backfill;
using BasketElo.Infrastructure.CurrentResults;
using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BasketElo.Infrastructure.Tests.CurrentResults;

public class CurrentResultsIngestionServiceTests
{
    [Fact]
    public async Task ManualTeamSearchIncludesStrongMatchesOutsideCompetitionCountry()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var team = new Team
        {
            Id = Guid.NewGuid(),
            CanonicalName = "BC Andorra",
            CountryCode = "AD"
        };
        var review = new CurrentResultReview
        {
            Id = Guid.NewGuid(),
            Source = "livescore",
            SourceGameId = "andorra-home-review",
            SourceDate = new DateOnly(2026, 9, 19),
            GameDateTimeUtc = new DateTime(2026, 9, 19, 18, 0, 0, DateTimeKind.Utc),
            CountryName = "Spain",
            CompetitionName = "Spanish League",
            HomeTeamName = "Basquet Club Andorra",
            AwayTeamName = "Unmapped Opponent",
            HomeTeamSourceId = "andorra-home",
            AwayTeamSourceId = "andorra-away",
            ResultStatus = CurrentResultStatuses.Finished,
            Reason = CurrentResultReviewReasons.UnresolvedHomeTeam,
            Status = CurrentResultReviewStatuses.Open
        };
        dbContext.AddRange(team, review);
        await dbContext.SaveChangesAsync();

        var candidate = new CurrentResultCandidate(
            "andorra-home-review", null, review.SourceDate, review.GameDateTimeUtc,
            review.CountryName, review.CompetitionName, null,
            review.HomeTeamName, review.AwayTeamName, review.HomeTeamSourceId, review.AwayTeamSourceId,
            80, 70, CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        var results = await service.GetReviewTeamCandidatesAsync(
            review.Id,
            "home",
            "andorra",
            CancellationToken.None);

        var match = Assert.Single(results);
        Assert.Equal(team.Id, match.TeamId);
        Assert.Equal("AD", match.CountryCode);
    }

    [Fact]
    public async Task FinishedLivescoreResultUpdatesScheduledFibaFixtureWithoutCreatingDuplicate()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "FIBA Basketball World Cup Qualifiers",
            CountryCode = null,
            EloPoolKey = "national-teams",
            IsActive = true
        };
        var homeTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Spain", CountryCode = "ES" };
        var awayTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Georgia", CountryCode = "GE" };
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = "2027",
            StartDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2027, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };
        var cycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "worldcup-2027",
            Family = "FIBA Basketball World Cup",
            EditionLabel = "2027",
            DisplayName = "FIBA Basketball World Cup 2027"
        };
        var planned = new Game
        {
            Id = Guid.NewGuid(),
            Source = "fiba",
            SourceGameId = "127004",
            SourceUrl = "https://www.fiba.basketball/en/events/fiba-basketball-world-cup-2027-european-qualifiers/games/127004",
            SourceSeasonKey = "2027",
            SourceRevision = "fiba-revision",
            ParserVersion = "fiba-parser-v1",
            CompetitionId = competition.Id,
            SeasonId = season.Id,
            TournamentCycleId = cycle.Id,
            GameDateTimeUtc = new DateTime(2026, 8, 28, 18, 0, 0, DateTimeKind.Utc),
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            Status = CurrentResultStatuses.Scheduled,
            EloEligible = false
        };

        dbContext.AddRange(competition, homeTeam, awayTeam, season, cycle, planned);
        await dbContext.SaveChangesAsync();

        var provider = new TestCurrentResultsProvider(new CurrentResultCandidate(
            "livescore-999",
            "https://www.livescores.com/basketball/event/999/",
            new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc),
            "World",
            "FIBA Basketball World Cup Qualifiers",
            "Window 4",
            "Spain",
            "Georgia",
            "team:world:spain",
            "team:world:georgia",
            88,
            76,
            CurrentResultStatuses.Finished,
            "FT",
            "livescore-revision",
            "livescore-test-v1"));
        var service = new CurrentResultsIngestionService(
            dbContext,
            provider,
            new TestBackfillCatalog(),
            new CleanIdentityHealthCheckService(),
            TimeProvider.System,
            NullLogger<CurrentResultsIngestionService>.Instance);

        var summary = await service.RunAsync(
            new DateOnly(2026, 8, 28),
            new DateOnly(2026, 8, 28),
            dryRun: false,
            CancellationToken.None);

        var games = await dbContext.Games.ToListAsync();
        var result = Assert.Single(games);
        Assert.Equal(planned.Id, result.Id);
        Assert.Equal("fiba", result.Source);
        Assert.Equal("127004", result.SourceGameId);
        Assert.Equal((short)88, result.HomeScore);
        Assert.Equal((short)76, result.AwayScore);
        Assert.Equal(CurrentResultStatuses.Finished, result.Status);
        Assert.Equal(planned.SourceUrl, result.SourceUrl);
        Assert.Equal("fiba-revision", result.SourceRevision);
        Assert.Equal(1, summary.GamesUpserted);
    }

    [Fact]
    public async Task FinishedLivescoreResultReusesAndPreservesAlreadyFinishedFibaFixture()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "FIBA Basketball World Cup Qualifiers",
            EloPoolKey = "national-teams",
            IsActive = true
        };
        var homeTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Dominican Republic", CountryCode = "DO" };
        var awayTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Chile", CountryCode = "CL" };
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = "2027",
            StartDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2027, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };
        var cycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "worldcup-2027",
            Family = "FIBA Basketball World Cup",
            EditionLabel = "2027",
            DisplayName = "FIBA Basketball World Cup 2027"
        };
        var official = new Game
        {
            Id = Guid.NewGuid(),
            Source = "fiba",
            SourceGameId = "127300",
            SourceUrl = "https://www.fiba.basketball/game/127300",
            SourceSeasonKey = "2027",
            SourceRevision = "fiba-revision",
            ParserVersion = "fiba-parser-v1",
            CompetitionId = competition.Id,
            SeasonId = season.Id,
            TournamentCycleId = cycle.Id,
            GameDateTimeUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            HomeScore = 101,
            AwayScore = 66,
            Status = CurrentResultStatuses.Finished,
            EloEligible = true
        };

        dbContext.AddRange(competition, homeTeam, awayTeam, season, cycle, official);
        await dbContext.SaveChangesAsync();

        var candidate = new CurrentResultCandidate(
            "1825128",
            "https://www.livescores.com/basketball/event/1825128/",
            new DateOnly(2026, 9, 1),
            official.GameDateTimeUtc,
            "World",
            competition.Name,
            "America: 2nd Round: Group E",
            homeTeam.CanonicalName,
            awayTeam.CanonicalName,
            "team:world:dominican-republic",
            "team:world:chile",
            99,
            66,
            CurrentResultStatuses.Finished,
            "FT",
            "livescore-revision",
            "livescore-test-v1");
        var service = new CurrentResultsIngestionService(
            dbContext,
            new TestCurrentResultsProvider(candidate),
            new TestBackfillCatalog(),
            new CleanIdentityHealthCheckService(),
            TimeProvider.System,
            NullLogger<CurrentResultsIngestionService>.Instance);

        var summary = await service.RunAsync(
            candidate.SourceDate,
            candidate.SourceDate,
            dryRun: false,
            CancellationToken.None);

        var result = Assert.Single(await dbContext.Games.ToListAsync());
        Assert.Equal(official.Id, result.Id);
        Assert.Equal("fiba", result.Source);
        Assert.Equal("127300", result.SourceGameId);
        Assert.Equal((short)101, result.HomeScore);
        Assert.Equal((short)66, result.AwayScore);
        Assert.Equal(CurrentResultStatuses.Finished, result.Status);
        Assert.Equal("fiba-revision", result.SourceRevision);
        Assert.Equal(0, summary.GamesUpserted);
    }

    [Fact]
    public async Task NewFibaCycleWithoutConfirmedCycleIsStoredOutsideElo()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "FIBA Basketball World Cup Qualifiers",
            EloPoolKey = "national-teams",
            IsActive = true
        };
        var homeTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Spain", CountryCode = "ES" };
        var awayTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Georgia", CountryCode = "GE" };
        dbContext.AddRange(competition, homeTeam, awayTeam);
        await dbContext.SaveChangesAsync();

        var candidate = new CurrentResultCandidate(
            "livescore-new-cycle",
            "https://www.livescores.com/basketball/event/new-cycle/",
            new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc),
            "World",
            competition.Name,
            "Window 4",
            "Spain",
            "Georgia",
            "team:world:spain",
            "team:world:georgia",
            88,
            76,
            CurrentResultStatuses.Finished,
            "FT",
            "new-cycle-revision",
            "livescore-test-v1");
        var service = new CurrentResultsIngestionService(
            dbContext,
            new TestCurrentResultsProvider(candidate),
            new TestBackfillCatalog(),
            new CleanIdentityHealthCheckService(),
            TimeProvider.System,
            NullLogger<CurrentResultsIngestionService>.Instance);

        var summary = await service.RunAsync(new DateOnly(2026, 8, 28), new DateOnly(2026, 8, 28), false, CancellationToken.None);

        var game = await dbContext.Games.SingleAsync();
        var review = await dbContext.CurrentResultReviews.SingleAsync();
        Assert.Equal(CurrentResultReviewReasons.TournamentCycleConfirmationRequired, review.Reason);
        Assert.Equal(CurrentResultReviewStatuses.Open, review.Status);
        Assert.False(game.EloEligible);
        Assert.Equal(CurrentResultReviewReasons.TournamentCycleConfirmationRequired, game.EloExclusionReason);
        Assert.Equal(0, summary.EloPoolsQueued);
        Assert.Empty(await dbContext.EloRebuildRuns.ToListAsync());
    }

    [Fact]
    public async Task ReviewAssignmentUpdatesSelectedFixtureAndQueuesEloRebuilds()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);

        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "FIBA Basketball World Cup Qualifiers",
            EloPoolKey = "national-teams",
            IsActive = true
        };
        var homeTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Spain", CountryCode = "ES" };
        var awayTeam = new Team { Id = Guid.NewGuid(), CanonicalName = "Georgia", CountryCode = "GE" };
        var season = new Season
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Label = "2027",
            StartDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2027, 12, 31, 23, 59, 59, DateTimeKind.Utc)
        };
        var planned = new Game
        {
            Id = Guid.NewGuid(),
            Source = "fiba",
            SourceGameId = "127004",
            CompetitionId = competition.Id,
            SeasonId = season.Id,
            GameDateTimeUtc = new DateTime(2026, 8, 28, 18, 0, 0, DateTimeKind.Utc),
            HomeTeamId = homeTeam.Id,
            AwayTeamId = awayTeam.Id,
            Status = "not started",
            EloEligible = false
        };
        var review = new CurrentResultReview
        {
            Id = Guid.NewGuid(),
            Source = "livescore",
            SourceGameId = "livescore-999",
            SourceDate = new DateOnly(2026, 8, 28),
            GameDateTimeUtc = new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc),
            CountryName = "World",
            CompetitionName = competition.Name,
            HomeTeamName = "Spain",
            AwayTeamName = "Georgia",
            HomeTeamSourceId = "team:world:spain",
            AwayTeamSourceId = "team:world:georgia",
            HomeScore = 88,
            AwayScore = 76,
            ResultStatus = CurrentResultStatuses.Finished,
            Reason = CurrentResultReviewReasons.AmbiguousPlannedFixture,
            Status = CurrentResultReviewStatuses.Open
        };
        dbContext.AddRange(competition, homeTeam, awayTeam, season, planned, review);
        await dbContext.SaveChangesAsync();

        var service = new CurrentResultsIngestionService(
            dbContext,
            new TestCurrentResultsProvider(null),
            new TestBackfillCatalog(),
            new CleanIdentityHealthCheckService(),
            TimeProvider.System,
            NullLogger<CurrentResultsIngestionService>.Instance);

        var result = await service.ResolveReviewAsync(
            review.Id,
            new CurrentResultReviewResolutionRequest("assign", planned.Id, "Confirmed against FIBA schedule."),
            CancellationToken.None);

        var updatedGame = await dbContext.Games.SingleAsync();
        var updatedReview = await dbContext.CurrentResultReviews.SingleAsync();
        Assert.Equal(CurrentResultReviewStatuses.Resolved, result.Status);
        Assert.Equal(planned.Id, result.AssignedGameId);
        Assert.Equal(CurrentResultReviewStatuses.Resolved, updatedReview.Status);
        Assert.Equal(planned.Id, updatedReview.AssignedGameId);
        Assert.Equal((short)88, updatedGame.HomeScore);
        Assert.Equal((short)76, updatedGame.AwayScore);
        Assert.Equal(CurrentResultStatuses.Finished, updatedGame.Status);
        Assert.Equal(2, result.EloRunsQueued);
        Assert.Equal(2, await dbContext.EloRebuildRuns.CountAsync());
        var aliases = await dbContext.TeamAliases.OrderBy(x => x.AliasName).ToListAsync();
        Assert.Equal(2, aliases.Count);
        Assert.Contains(aliases, x => x.TeamId == homeTeam.Id && x.SourceTeamId == review.HomeTeamSourceId);
        Assert.Contains(aliases, x => x.TeamId == awayTeam.Id && x.SourceTeamId == review.AwayTeamSourceId);
    }

    [Fact]
    public async Task UnknownCompetitionCreatesOnlyCompetitionReview()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "unknown-competition-game", null, new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc), "USA", "WNBA", null,
            "Minnesota Lynx", "Los Angeles Sparks", "home-source", "away-source", 88, 76,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        var review = await dbContext.CurrentResultReviews.SingleAsync();
        Assert.Equal(CurrentResultReviewReasons.UnknownCompetition, review.Reason);
        Assert.Equal(1, summary.ReviewsOpened);
        Assert.Empty(await dbContext.Games.ToListAsync());
        Assert.Empty(await dbContext.TeamAliases.ToListAsync());

        review.Status = CurrentResultReviewStatuses.Ignored;
        review.ResolutionAction = "ignore";
        await dbContext.SaveChangesAsync();
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);
        Assert.Equal(CurrentResultReviewStatuses.Open, (await dbContext.CurrentResultReviews.SingleAsync()).Status);
    }

    [Fact]
    public async Task UnsupportedCompetitionIsSkippedBeforeTeamResolution()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition
        {
            Id = Guid.NewGuid(), Name = "WNBA", CountryCode = "US",
            SupportPolicy = CompetitionSupportPolicies.Unsupported
        };
        dbContext.Competitions.Add(competition);
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "unsupported-competition-game", null, new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc), "USA", "WNBA", null,
            "Minnesota Lynx", "Los Angeles Sparks", "home-source", "away-source", 88, 76,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Equal(1, summary.UnsupportedSkipped);
        Assert.Equal(0, summary.ReviewsOpened);
        Assert.Empty(await dbContext.CurrentResultReviews.ToListAsync());
        Assert.Empty(await dbContext.TeamAliases.ToListAsync());
    }

    [Fact]
    public async Task ItalySerieA2IsSkippedInsteadOfMatchingTheTopDivision()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var legaA = new Competition
        {
            Id = Guid.NewGuid(), Name = "Lega A", CountryCode = "IT", EloPoolKey = "europe-clubs"
        };
        var serieA = new Competition
        {
            Id = Guid.NewGuid(), Name = "Serie A", CountryCode = "IT", EloPoolKey = "europe-clubs"
        };
        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Basket Torino", CountryCode = "IT" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Fortitudo Bologna", CountryCode = "IT" };
        dbContext.AddRange(legaA, serieA, home, away);
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "italy-serie-a2-game", null, new DateOnly(2026, 9, 20),
            new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc), "Italy", "Serie A2", null,
            home.CanonicalName, away.CanonicalName, "home-source", "away-source", 82, 77,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Equal(1, summary.UnsupportedSkipped);
        Assert.Equal(0, summary.GamesUpserted);
        Assert.Equal(0, summary.ReviewsOpened);
        Assert.Empty(await dbContext.Games.ToListAsync());
        Assert.Empty(await dbContext.CurrentResultReviews.ToListAsync());
    }

    [Fact]
    public async Task ItalySerieA2RemovesAnExistingReviewFromResultsMatching()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "existing-italy-serie-a2-review", null, new DateOnly(2026, 9, 20),
            new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc), "Italy", "Serie A2", null,
            "Basket Torino", "Fortitudo Bologna", "home-source", "away-source", 82, 77,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        dbContext.CurrentResultReviews.Add(new CurrentResultReview
        {
            Id = Guid.NewGuid(),
            Source = "livescore",
            SourceGameId = candidate.SourceGameId,
            SourceDate = candidate.SourceDate,
            GameDateTimeUtc = candidate.GameDateTimeUtc,
            CountryName = candidate.CountryName,
            CompetitionName = candidate.CompetitionName,
            HomeTeamName = candidate.HomeTeamName,
            AwayTeamName = candidate.AwayTeamName,
            HomeTeamSourceId = candidate.HomeTeamSourceId,
            AwayTeamSourceId = candidate.AwayTeamSourceId,
            ResultStatus = candidate.Status,
            Reason = CurrentResultReviewReasons.UnresolvedHomeTeam,
            Status = CurrentResultReviewStatuses.Open
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Equal(1, summary.UnsupportedSkipped);
        Assert.Empty(await dbContext.CurrentResultReviews.ToListAsync());
    }

    [Fact]
    public async Task SourceCompetitionIdAliasMatchesBeforeTeamResolution()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition { Id = Guid.NewGuid(), Name = "Women's National Basketball Association", CountryCode = "US" };
        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Minnesota Lynx", CountryCode = "US" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Los Angeles Sparks", CountryCode = "US" };
        dbContext.AddRange(competition, home, away, new CompetitionAlias
        {
            Id = Guid.NewGuid(), CompetitionId = competition.Id, Source = "livescore",
            SourceCompetitionId = "league-wnba", AliasName = "WNBA"
        });
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "aliased-competition-game", null, new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc), "USA", "WNBA: Regular Season", null,
            home.CanonicalName, away.CanonicalName, "home-source", "away-source", 88, 76,
            CurrentResultStatuses.Finished, "FT", "revision", "parser", "LEAGUE-WNBA");
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Equal(1, summary.GamesUpserted);
        Assert.Empty(await dbContext.CurrentResultReviews.ToListAsync());
        Assert.Equal(competition.Id, (await dbContext.Games.SingleAsync()).CompetitionId);
    }

    [Fact]
    public async Task CompetitionNameAliasDoesNotCrossCountryBoundary()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var czechNbl = new Competition
        {
            Id = Guid.NewGuid(), Name = "NBL", CountryCode = "CZ", EloPoolKey = "europe-clubs"
        };
        var unrelatedNbl = new Competition
        {
            Id = Guid.NewGuid(), Name = "NBL", CountryCode = null, IsActive = false,
            SupportPolicy = CompetitionSupportPolicies.Unsupported
        };
        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "Nymburk", CountryCode = "CZ" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Decin", CountryCode = "CZ" };
        dbContext.AddRange(czechNbl, unrelatedNbl, home, away, new CompetitionAlias
        {
            Id = Guid.NewGuid(), CompetitionId = unrelatedNbl.Id, Source = "livescore",
            SourceCompetitionId = string.Empty, AliasName = "NBL"
        });
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "czech-nbl-game", null, new DateOnly(2026, 9, 18),
            new DateTime(2026, 9, 18, 18, 0, 0, DateTimeKind.Utc), "Czech Republic", "NBL", null,
            home.CanonicalName, away.CanonicalName, "home-source", "away-source", 82, 77,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Equal(1, summary.GamesUpserted);
        Assert.Equal(0, summary.ReviewsOpened);
        Assert.Equal(czechNbl.Id, (await dbContext.Games.SingleAsync()).CompetitionId);
    }

    [Fact]
    public async Task IgnoringUnmatchedCompetitionCreatesUnsupportedDecisionAndHidesReviews()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "ignore-competition-game", null, new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc), "USA", "WNBA", null,
            "Minnesota Lynx", "Los Angeles Sparks", "home-source", "away-source", null, null,
            CurrentResultStatuses.Scheduled, "18:30", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        var ignored = await service.IgnoreUnmatchedCompetitionAsync(
            new IgnoreUnmatchedCompetitionRequest("livescore", null, "USA", "WNBA"),
            CancellationToken.None);

        Assert.Equal(1, ignored);
        Assert.Empty(await service.GetUnmatchedCompetitionsAsync(CancellationToken.None));
        Assert.Equal(CurrentResultReviewStatuses.Ignored, (await dbContext.CurrentResultReviews.SingleAsync()).Status);
        Assert.Equal(CompetitionSupportPolicies.Unsupported, (await dbContext.Competitions.SingleAsync()).SupportPolicy);
        Assert.False((await dbContext.Competitions.SingleAsync()).IsActive);
        Assert.Single(await dbContext.CompetitionAliases.ToListAsync());

        var rerun = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);
        Assert.Equal(1, rerun.UnsupportedSkipped);
        Assert.Equal(0, rerun.ReviewsOpened);

    }

    [Fact]
    public async Task IgnoringExistingInactiveMexicanCompetitionIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "ignore-lnbp-game", null, new DateOnly(2026, 9, 14),
            new DateTime(2026, 9, 14, 1, 0, 0, DateTimeKind.Utc), "Mexico", "LNBP", null,
            "A", "B", "home-source", "away-source", null, null,
            CurrentResultStatuses.Scheduled, "01:00", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        // Simulate a prior click that created the inactive placeholder before
        // the request was retried.
        var existing = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "LNBP",
            CountryCode = "MX",
            Type = "current-results",
            SupportPolicy = CompetitionSupportPolicies.Unsupported,
            IsActive = false
        };
        dbContext.Competitions.Add(existing);
        await dbContext.SaveChangesAsync();

        var ignored = await service.IgnoreUnmatchedCompetitionAsync(
            new IgnoreUnmatchedCompetitionRequest("livescore", null, "Mexico", "LNBP"),
            CancellationToken.None);

        Assert.Equal(1, ignored);
        Assert.Single(await dbContext.Competitions.ToListAsync());
        Assert.Equal(existing.Id, (await dbContext.CompetitionAliases.SingleAsync()).CompetitionId);
        Assert.Equal(CurrentResultReviewStatuses.Ignored, (await dbContext.CurrentResultReviews.SingleAsync()).Status);
    }

    [Fact]
    public async Task IgnoringCompetitionReusesExistingAliasWhenCountryCodeWasAddedLater()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "ignore-kvindebasketligaen-game", null, new DateOnly(2026, 9, 20),
            new DateTime(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc), "Denmark", "Kvindebasketligaen", null,
            "Sisu W", "BK Amager W", "home-source", "away-source", null, null,
            CurrentResultStatuses.Scheduled, "09:00", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        // This is the legacy shape that caused the conflict: the existing
        // unsupported placeholder has no country code, but already owns the
        // Livescore alias.
        var existing = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "Kvindebasketligaen",
            CountryCode = null,
            Type = "current-results",
            SupportPolicy = CompetitionSupportPolicies.Unsupported,
            IsActive = false
        };
        dbContext.AddRange(existing, new CompetitionAlias
        {
            Id = Guid.NewGuid(),
            CompetitionId = existing.Id,
            Source = "livescore",
            SourceCompetitionId = string.Empty,
            AliasName = "Kvindebasketligaen"
        });
        await dbContext.SaveChangesAsync();

        var ignored = await service.IgnoreUnmatchedCompetitionAsync(
            new IgnoreUnmatchedCompetitionRequest("livescore", null, "Denmark", "Kvindebasketligaen"),
            CancellationToken.None);

        Assert.Equal(1, ignored);
        Assert.Single(await dbContext.Competitions.ToListAsync());
        Assert.Equal(existing.Id, (await dbContext.CompetitionAliases.SingleAsync()).CompetitionId);
        Assert.Equal(CurrentResultReviewStatuses.Ignored, (await dbContext.CurrentResultReviews.SingleAsync()).Status);
        Assert.Equal("DK", existing.CountryCode);

        var rerun = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);
        Assert.Equal(1, rerun.UnsupportedSkipped);
        Assert.Equal(0, rerun.ReviewsOpened);
        Assert.Equal(CurrentResultReviewStatuses.Ignored, (await dbContext.CurrentResultReviews.SingleAsync()).Status);
    }

    [Fact]
    public async Task SavedUnsupportedAliasSkipsFutureImportsWhenLegacyCompetitionHasNoCountryCode()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "Kvindebasketligaen",
            CountryCode = null,
            Type = "current-results",
            SupportPolicy = CompetitionSupportPolicies.Unsupported,
            IsActive = false
        };
        dbContext.AddRange(competition, new CompetitionAlias
        {
            Id = Guid.NewGuid(),
            CompetitionId = competition.Id,
            Source = "livescore",
            SourceCompetitionId = string.Empty,
            AliasName = "Kvindebasketligaen"
        });
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "future-kvindebasketligaen-game", null, new DateOnly(2026, 9, 24),
            new DateTime(2026, 9, 24, 17, 0, 0, DateTimeKind.Utc), "Denmark", "Kvindebasketligaen", null,
            "Sisu W", "BK Amager W", "home-source", "away-source", null, null,
            CurrentResultStatuses.Scheduled, "17:00", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        var summary = await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Equal(1, summary.UnsupportedSkipped);
        Assert.Equal(0, summary.ReviewsOpened);
        Assert.Empty(await dbContext.CurrentResultReviews.ToListAsync());
    }

    [Fact]
    public async Task MergingUnmatchedCompetitionAddsAliasAndReclassifiesRemainingReviews()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var target = new Competition { Id = Guid.NewGuid(), Name = "Women's National Basketball Association", CountryCode = "US" };
        dbContext.Competitions.Add(target);
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "merge-competition-game", null, new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc), "USA", "WNBA", null,
            "Minnesota Lynx", "Los Angeles Sparks", "home-source", "away-source", null, null,
            CurrentResultStatuses.Scheduled, "18:30", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        var updated = await service.MergeUnmatchedCompetitionAsync(
            new MergeUnmatchedCompetitionRequest("livescore", null, "USA", "WNBA", target.Id),
            CancellationToken.None);

        Assert.Equal(1, updated);
        Assert.Empty(await service.GetUnmatchedCompetitionsAsync(CancellationToken.None));
        Assert.Equal(target.Id, (await dbContext.CompetitionAliases.SingleAsync()).CompetitionId);
        var review = await dbContext.CurrentResultReviews.SingleAsync();
        Assert.Equal(CurrentResultReviewStatuses.Open, review.Status);
        Assert.NotEqual(CurrentResultReviewReasons.UnknownCompetition, review.Reason);
        Assert.Null(review.ResolutionAction);
    }

    [Fact]
    public async Task ReprocessingPreviouslyMergedCompetitionReviewsCreatesNowMappableGames()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var target = new Competition
        {
            Id = Guid.NewGuid(), Name = "Women's National Basketball Association", CountryCode = "US"
        };
        dbContext.AddRange(
            target,
            new Team { Id = Guid.NewGuid(), CanonicalName = "Minnesota Lynx", CountryCode = "US" },
            new Team { Id = Guid.NewGuid(), CanonicalName = "Los Angeles Sparks", CountryCode = "US" });
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "legacy-merged-competition-game", null, new DateOnly(2026, 8, 28),
            new DateTime(2026, 8, 28, 18, 30, 0, DateTimeKind.Utc), "USA", "WNBA", null,
            "Minnesota Lynx", "Los Angeles Sparks", "home-source", "away-source", 88, 76,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        var review = await dbContext.CurrentResultReviews.SingleAsync();
        dbContext.CompetitionAliases.Add(new CompetitionAlias
        {
            Id = Guid.NewGuid(),
            CompetitionId = target.Id,
            Source = "livescore",
            SourceCompetitionId = string.Empty,
            AliasName = "WNBA",
            CreatedAtUtc = DateTime.UtcNow
        });
        review.ResolutionAction = "merge";
        await dbContext.SaveChangesAsync();

        var processed = await service.ReprocessMergedCompetitionReviewsAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(CurrentResultReviewStatuses.Resolved, review.Status);
        Assert.Empty(review.Reason);
        Assert.NotNull(review.AssignedGameId);
        Assert.Single(await dbContext.Games.ToListAsync());
    }

    [Fact]
    public async Task MergedCompetitionAliasResolvesWhenFeedUsesCompetitionAsCountryName()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var target = new Competition
        {
            Id = Guid.NewGuid(), Name = "VTB United League", CountryCode = "RU"
        };
        dbContext.AddRange(
            target,
            new Team { Id = Guid.NewGuid(), CanonicalName = "Avtodor", CountryCode = "RU" },
            new Team { Id = Guid.NewGuid(), CanonicalName = "BC Samara", CountryCode = "RU" });
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "vtb-league-game", null, new DateOnly(2026, 9, 19),
            new DateTime(2026, 9, 19, 16, 0, 0, DateTimeKind.Utc), "VTB United League", "VTB United League", null,
            "Avtodor", "BC Samara", "home-vtb", "away-vtb", 85, 79,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);
        Assert.Equal(CurrentResultReviewReasons.UnknownCompetition, (await dbContext.CurrentResultReviews.SingleAsync()).Reason);

        var updated = await service.MergeUnmatchedCompetitionAsync(
            new MergeUnmatchedCompetitionRequest(
                "livescore", null, "VTB United League", "VTB United League", target.Id),
            CancellationToken.None);

        Assert.Equal(1, updated);
        Assert.Empty(await service.GetUnmatchedCompetitionsAsync(CancellationToken.None));
        Assert.Equal(CurrentResultReviewStatuses.Resolved, (await dbContext.CurrentResultReviews.SingleAsync()).Status);
        Assert.Equal(target.Id, (await dbContext.Games.SingleAsync()).CompetitionId);
    }

    [Fact]
    public async Task CreatingCanonicalTeamDefaultsToSuggestedCompetitionCountry()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition
        {
            Id = Guid.NewGuid(), Name = "VTB United League", CountryCode = "RU"
        };
        var review = new CurrentResultReview
        {
            Id = Guid.NewGuid(),
            Source = "livescore",
            SourceGameId = "vtb-new-home-team",
            SourceDate = new DateOnly(2026, 9, 19),
            GameDateTimeUtc = new DateTime(2026, 9, 19, 16, 0, 0, DateTimeKind.Utc),
            CountryName = "VTB United League",
            CompetitionName = "VTB United League",
            SuggestedCompetitionName = competition.Name,
            SuggestedCompetitionCountryCode = competition.CountryCode,
            HomeTeamName = "New VTB Club",
            AwayTeamName = "Unmapped Opponent",
            HomeTeamSourceId = "new-vtb-home",
            AwayTeamSourceId = "unmapped-away",
            HomeScore = 80,
            AwayScore = 70,
            ResultStatus = CurrentResultStatuses.Finished,
            Reason = CurrentResultReviewReasons.UnresolvedHomeTeam,
            Status = CurrentResultReviewStatuses.Open
        };
        dbContext.AddRange(
            competition,
            new CompetitionAlias
            {
                Id = Guid.NewGuid(), CompetitionId = competition.Id, Source = "livescore",
                SourceCompetitionId = string.Empty, AliasName = "VTB United League"
            },
            review);
        await dbContext.SaveChangesAsync();
        var candidate = new CurrentResultCandidate(
            "vtb-new-home-team", null, review.SourceDate, review.GameDateTimeUtc,
            review.CountryName, review.CompetitionName, null, review.HomeTeamName, review.AwayTeamName,
            review.HomeTeamSourceId, review.AwayTeamSourceId, review.HomeScore, review.AwayScore,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        await service.CreateReviewTeamAsync(
            review.Id,
            new CurrentResultReviewTeamCreateRequest("home", "New VTB Club"),
            CancellationToken.None);

        Assert.Equal("RU", (await dbContext.Teams.SingleAsync()).CountryCode);
    }

    [Fact]
    public async Task MergingCanCreateCompetitionAssignCycleAndReprocessCandidate()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "create-competition-cycle-game", null, new DateOnly(2029, 8, 28),
            new DateTime(2029, 8, 28, 18, 30, 0, DateTimeKind.Utc), "World",
            "FIBA Basketball World Cup Qualifiers", "Window 1",
            "Spain", "Georgia", "home-source", "away-source", 88, 76,
            CurrentResultStatuses.Finished, "FT", "revision", "parser");
        dbContext.AddRange(
            new Team { Id = Guid.NewGuid(), CanonicalName = "Spain", CountryCode = "ES" },
            new Team { Id = Guid.NewGuid(), CanonicalName = "Georgia", CountryCode = "GE" });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, candidate);

        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);
        var updated = await service.MergeUnmatchedCompetitionAsync(
            new MergeUnmatchedCompetitionRequest(
                "livescore", null, "World", candidate.CompetitionName, null,
                new CreateCompetitionFromMergeRequest(
                    "FIBA Basketball World Cup Qualifiers", "qualifier", null, "national-teams", 1,
                    CompetitionSupportPolicies.Supported),
                null, "FIBA Basketball World Cup", "2031"),
            CancellationToken.None);

        Assert.Equal(1, updated);
        var cycle = await dbContext.TournamentCycles.SingleAsync();
        Assert.Equal("worldcup-2031", cycle.Key);
        Assert.Equal("FIBA Basketball World Cup 2031", cycle.DisplayName);
        var review = await dbContext.CurrentResultReviews.SingleAsync();
        Assert.Equal(cycle.Id, review.TournamentCycleId);
        Assert.Equal(CurrentResultReviewStatuses.Resolved, review.Status);
        Assert.Single(await dbContext.Games.ToListAsync());

        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        var game = await dbContext.Games.SingleAsync();
        Assert.Equal(cycle.Id, game.TournamentCycleId);
        Assert.True(game.EloEligible);
    }

    [Fact]
    public async Task MergingAutoAssignsAnUnambiguousPlannedFixture()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var target = new Competition
        {
            Id = Guid.NewGuid(),
            Name = "FIBA World Cup Qualifiers",
            CountryCode = null,
            EloPoolKey = "national-teams"
        };
        var home = new Team { Id = Guid.NewGuid(), CanonicalName = "France", CountryCode = "FR" };
        var away = new Team { Id = Guid.NewGuid(), CanonicalName = "Slovenia", CountryCode = "SI" };
        var cycle = new TournamentCycle
        {
            Id = Guid.NewGuid(),
            Key = "worldcup-2027",
            Family = "FIBA Basketball World Cup",
            EditionLabel = "2027",
            DisplayName = "FIBA Basketball World Cup 2027"
        };
        dbContext.AddRange(
            target,
            home,
            away,
            cycle,
            new Game
            {
                Id = Guid.NewGuid(),
                Source = "fiba",
                SourceGameId = "planned-france-slovenia",
                CompetitionId = target.Id,
                SeasonId = Guid.NewGuid(),
                TournamentCycleId = cycle.Id,
                GameDateTimeUtc = new DateTime(2026, 8, 27, 18, 30, 0, DateTimeKind.Utc),
                HomeTeamId = home.Id,
                AwayTeamId = away.Id,
                Status = CurrentResultStatuses.Scheduled
            });
        await dbContext.SaveChangesAsync();

        var candidate = new CurrentResultCandidate(
            "merge-auto-assign-game", null, new DateOnly(2026, 8, 27),
            new DateTime(2026, 8, 27, 8, 0, 0, DateTimeKind.Utc), "World", "Qualification", "Qualification",
            "France", "Slovenia", "france-live", "slovenia-live", null, null,
            CurrentResultStatuses.Scheduled, "08:00", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        var updated = await service.MergeUnmatchedCompetitionAsync(
            new MergeUnmatchedCompetitionRequest(
                "livescore", null, "World", candidate.CompetitionName, target.Id,
                null, cycle.Id, null, null),
            CancellationToken.None);

        Assert.Equal(1, updated);
        var review = await dbContext.CurrentResultReviews.SingleAsync();
        var game = await dbContext.Games.SingleAsync(x => x.Source == "fiba");
        Assert.Equal(CurrentResultReviewStatuses.Resolved, review.Status);
        Assert.Equal("merge_auto_assign", review.ResolutionAction);
        Assert.Equal(game.Id, review.AssignedGameId);
        Assert.Equal(cycle.Id, game.TournamentCycleId);
        Assert.Equal(CurrentResultStatuses.Scheduled, game.Status);
    }

    [Fact]
    public async Task MergeRejectsCycleFromTheWrongKnownFamily()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var candidate = new CurrentResultCandidate(
            "wrong-cycle-family-game", null, new DateOnly(2029, 8, 28),
            new DateTime(2029, 8, 28, 18, 30, 0, DateTimeKind.Utc), "World",
            "FIBA Basketball World Cup Qualifiers", "Window 1",
            "Spain", "Georgia", "home-source", "away-source", null, null,
            CurrentResultStatuses.Scheduled, "18:30", "revision", "parser");
        var service = CreateService(dbContext, candidate);
        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.MergeUnmatchedCompetitionAsync(
            new MergeUnmatchedCompetitionRequest(
                "livescore", null, "World", candidate.CompetitionName, null,
                new CreateCompetitionFromMergeRequest(
                    "FIBA Basketball World Cup Qualifiers", "qualifier", null, "national-teams", 1,
                    CompetitionSupportPolicies.Supported),
                null, "Olympics", "2028"),
            CancellationToken.None));
    }

    [Fact]
    public async Task HighConfidenceRecentTeamNameIsMappedAutomatically()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition
        {
            Id = Guid.NewGuid(), Name = "ACB", CountryCode = "ES", EloPoolKey = "europe-clubs", IsActive = true
        };
        var priorSeason = new Season
        {
            Id = Guid.NewGuid(), CompetitionId = competition.Id, Label = "2025-2026",
            StartDateUtc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };
        var baskonia = new Team { Id = Guid.NewGuid(), CanonicalName = "Baskonia", CountryCode = "ES" };
        var barcelona = new Team { Id = Guid.NewGuid(), CanonicalName = "Barcelona", CountryCode = "ES" };
        var priorGame = new Game
        {
            Id = Guid.NewGuid(), Source = "historical", SourceGameId = "prior-acb", CompetitionId = competition.Id,
            SeasonId = priorSeason.Id, GameDateTimeUtc = new DateTime(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc),
            HomeTeamId = baskonia.Id, AwayTeamId = barcelona.Id, Status = CurrentResultStatuses.Finished,
            HomeScore = 88, AwayScore = 82
        };
        dbContext.AddRange(competition, priorSeason, baskonia, barcelona, priorGame);
        await dbContext.SaveChangesAsync();

        var candidate = new CurrentResultCandidate(
            "acb-new", null, new DateOnly(2026, 9, 20),
            new DateTime(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc), "Spain", "ACB", null,
            "Saski Baskonia", "Barcelona", "team:spain:saski-baskonia", "team:spain:barcelona",
            null, null, CurrentResultStatuses.Scheduled, "scheduled", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Empty(await dbContext.CurrentResultReviews.ToListAsync());
        var alias = await dbContext.TeamAliases.SingleAsync(x => x.SourceTeamId == "team:spain:saski-baskonia");
        Assert.Equal(baskonia.Id, alias.TeamId);
        Assert.Equal("automatic_fuzzy", alias.MappingMethod);
        Assert.InRange(alias.MappingConfidence!.Value, 95, 100);
    }

    [Fact]
    public async Task ConflictingAutomaticTeamMappingsOpenReviewInsteadOfCreatingSelfGame()
    {
        var options = new DbContextOptionsBuilder<BasketEloDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new BasketEloDbContext(options);
        var competition = new Competition
        {
            Id = Guid.NewGuid(), Name = "Premijer liga", CountryCode = "HR", EloPoolKey = "europe-clubs", IsActive = true
        };
        var priorSeason = new Season
        {
            Id = Guid.NewGuid(), CompetitionId = competition.Id, Label = "2025-2026",
            StartDateUtc = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDateUtc = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc)
        };
        var dubrava = new Team { Id = Guid.NewGuid(), CanonicalName = "Dubrava", CountryCode = "HR" };
        var opponent = new Team { Id = Guid.NewGuid(), CanonicalName = "Zadar", CountryCode = "HR" };
        dbContext.AddRange(
            competition,
            priorSeason,
            dubrava,
            opponent,
            new Game
            {
                Id = Guid.NewGuid(), Source = "historical", SourceGameId = "prior-dubrava", CompetitionId = competition.Id,
                SeasonId = priorSeason.Id, GameDateTimeUtc = new DateTime(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc),
                HomeTeamId = dubrava.Id, AwayTeamId = opponent.Id, Status = CurrentResultStatuses.Finished,
                HomeScore = 80, AwayScore = 75
            });
        await dbContext.SaveChangesAsync();

        var candidate = new CurrentResultCandidate(
            "croatia-self-map", null, new DateOnly(2026, 9, 26),
            new DateTime(2026, 9, 26, 16, 0, 0, DateTimeKind.Utc), "Croatia", "Premijer liga", null,
            "Dubrava", "Dubravaa", "team:croatia:dubrava", "team:croatia:opponent",
            null, null, CurrentResultStatuses.Scheduled, "scheduled", "revision", "parser");
        var service = CreateService(dbContext, candidate);

        await service.RunAsync(candidate.SourceDate, candidate.SourceDate, false, CancellationToken.None);

        Assert.Empty(await dbContext.Games.Where(x => x.Source == "livescore").ToListAsync());
        Assert.Empty(await dbContext.TeamAliases.Where(x => x.Source == "livescore").ToListAsync());
        var review = await dbContext.CurrentResultReviews.SingleAsync();
        Assert.Equal(CurrentResultReviewStatuses.Open, review.Status);
        Assert.Equal(CurrentResultReviewReasons.ConflictingTeamMapping, review.Reason);
    }

    private static CurrentResultsIngestionService CreateService(BasketEloDbContext dbContext, CurrentResultCandidate candidate) =>
        new(
            dbContext,
            new TestCurrentResultsProvider(candidate),
            new TestBackfillCatalog(),
            new CleanIdentityHealthCheckService(),
            TimeProvider.System,
            NullLogger<CurrentResultsIngestionService>.Instance);

    private sealed class TestCurrentResultsProvider(CurrentResultCandidate? candidate) : ICurrentResultsProvider
    {
        public string Source => "livescore";

        public Task<CurrentResultFetchResult> FetchAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult(new CurrentResultFetchResult(date, candidate?.SourceUrl ?? "https://www.livescores.com", candidate?.SourceRevision ?? "test", candidate is null ? [] : [candidate]));
    }

    private sealed class TestBackfillCatalog : IBackfillCatalog
    {
        private static readonly ConfiguredBackfillLeague League = new(
            "fiba",
            "World",
            "FIBA Basketball World Cup Qualifiers",
            "World: FIBA Basketball World Cup Qualifiers",
            "2027",
            EloPoolKey: "national-teams",
            ExplicitSeasons: ["2027"]);

        public IReadOnlyCollection<ConfiguredBackfillLeague> GetLeagues() => [League];

        public IReadOnlyCollection<string> GetSeasonsForLeague(ConfiguredBackfillLeague league) => ["2027"];
    }

    private sealed class CleanIdentityHealthCheckService : IIdentityHealthCheckService
    {
        public Task<IdentityHealthCheckRunDto> RunAsync(IdentityHealthCheckRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new IdentityHealthCheckRunDto(
                Guid.NewGuid(),
                request.Source,
                request.Season,
                request.CountryCode,
                request.CompetitionId,
                "test",
                "test",
                IdentityHealthCheckStatus.Clean,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                [],
                request.Force,
                DateTime.UtcNow,
                null));

        public Task<IdentityHealthOptionsDto> GetOptionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityHealthCheckRunDto>> GetRunsAsync(IdentityHealthCheckQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityHealthCheckFindingDto>> GetFindingsAsync(IdentityFindingQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityReviewCandidateDto>> GetReviewCandidatesAsync(IdentityReviewQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityReviewCandidateDto> ResolveReviewCandidateAsync(ResolveIdentityPairRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IdentityDistinctTeamsDecisionDto>> GetDistinctTeamDecisionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityEvidenceGamesResponseDto> GetEvidenceGamesAsync(Guid findingId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityTeamMergeResultDto> MergeTeamsAsync(Guid sourceTeamId, Guid targetTeamId, bool confirmMergeWithRatings, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityHealthCheckFindingDto> ResolveFindingAsync(Guid findingId, ResolveIdentityFindingRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RemoveDistinctTeamDecisionAsync(Guid leftTeamId, Guid rightTeamId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRunAsync(Guid runId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateChangedScopeAsync(IdentityChangedScope changedScope, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
