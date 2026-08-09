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
            Status = CurrentResultStatuses.Scheduled,
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
        Assert.Equal(3, result.EloRunsQueued);
        Assert.Equal(3, await dbContext.EloRebuildRuns.CountAsync());
    }

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
