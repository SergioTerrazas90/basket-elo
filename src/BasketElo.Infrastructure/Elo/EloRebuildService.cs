using System.Text.Json;
using BasketElo.Domain.Elo;
using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BasketElo.Infrastructure.Elo;

public class EloRebuildService(
    BasketEloDbContext dbContext,
    IEloRebuildNotificationPublisher notificationPublisher,
    ILogger<EloRebuildService> logger) : IEloRebuildService
{
    public async Task<EloRebuildResult> RebuildAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await dbContext.EloRebuildRuns.SingleAsync(x => x.Id == runId, cancellationToken);
        if (run.Status != EloRebuildRunStatus.Running)
        {
            throw new InvalidOperationException($"ELO rebuild run '{runId}' is not running.");
        }

        var rulesetVersion = run.RulesetVersion;

        try
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            await dbContext.RatingHistories
                .Where(x => x.RulesetVersion == rulesetVersion)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.RankingSnapshots
                .Where(x => x.RulesetVersion == rulesetVersion)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.TeamRatings
                .Where(x => x.RulesetVersion == rulesetVersion)
                .ExecuteDeleteAsync(cancellationToken);

            var games = await dbContext.Games
                .AsNoTracking()
                .Where(x => x.HomeScore.HasValue && x.AwayScore.HasValue && x.HomeScore != x.AwayScore)
                .OrderBy(x => x.GameDateTimeUtc)
                .ThenBy(x => x.Source)
                .ThenBy(x => x.SourceGameId)
                .ThenBy(x => x.Id)
                .Select(x => new RatedGame(
                    x.Id,
                    x.GameDateTimeUtc,
                    x.HomeTeamId,
                    x.AwayTeamId,
                    x.HomeScore!.Value,
                    x.AwayScore!.Value))
                .ToListAsync(cancellationToken);

            var ratings = new Dictionary<Guid, RatingState>();
            var histories = new List<RatingHistory>(games.Count * 2);
            var latestGameDate = games.Count > 0 ? games[^1].GameDateTimeUtc : (DateTime?)null;

            foreach (var game in games)
            {
                var home = GetRatingState(ratings, game.HomeTeamId);
                var away = GetRatingState(ratings, game.AwayTeamId);
                var calculation = EloCalculator.Calculate(
                    game.HomeScore,
                    game.AwayScore,
                    home.Elo,
                    away.Elo,
                    rulesetVersion);

                var homePreElo = home.Elo;
                var awayPreElo = away.Elo;
                var homeGamesPlayedBefore = home.GamesPlayed;
                var awayGamesPlayedBefore = away.GamesPlayed;

                home.Elo += calculation.HomeDelta;
                away.Elo -= calculation.HomeDelta;
                home.GamesPlayed += 1;
                away.GamesPlayed += 1;
                home.LastGameId = game.Id;
                away.LastGameId = game.Id;

                var positions = GetPositions(ratings, game.HomeTeamId, game.AwayTeamId);

                histories.Add(new RatingHistory
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    TeamId = game.HomeTeamId,
                    OpponentTeamId = game.AwayTeamId,
                    RulesetVersion = rulesetVersion,
                    GameDateTimeUtc = game.GameDateTimeUtc,
                    PreElo = RoundRating(homePreElo),
                    PostElo = RoundRating(home.Elo),
                    EloDelta = RoundRating(calculation.HomeDelta),
                    KFactorUsed = EloCalculator.KFactor,
                    ExpectedScore = RoundProbability(calculation.ExpectedHomeResult),
                    ActualScore = calculation.HomeActualResult,
                    MarginMultiplier = RoundMultiplier(calculation.MarginMultiplier),
                    CompetitionWeight = EloCalculator.CompetitionWeight,
                    GamesPlayedBefore = homeGamesPlayedBefore,
                    RatingPositionAfter = positions.HomePosition,
                    CreatedAtUtc = DateTime.UtcNow
                });

                histories.Add(new RatingHistory
                {
                    Id = Guid.NewGuid(),
                    GameId = game.Id,
                    TeamId = game.AwayTeamId,
                    OpponentTeamId = game.HomeTeamId,
                    RulesetVersion = rulesetVersion,
                    GameDateTimeUtc = game.GameDateTimeUtc,
                    PreElo = RoundRating(awayPreElo),
                    PostElo = RoundRating(away.Elo),
                    EloDelta = RoundRating(-calculation.HomeDelta),
                    KFactorUsed = EloCalculator.KFactor,
                    ExpectedScore = RoundProbability(1m - calculation.ExpectedHomeResult),
                    ActualScore = 1m - calculation.HomeActualResult,
                    MarginMultiplier = RoundMultiplier(calculation.MarginMultiplier),
                    CompetitionWeight = EloCalculator.CompetitionWeight,
                    GamesPlayedBefore = awayGamesPlayedBefore,
                    RatingPositionAfter = positions.AwayPosition,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            dbContext.RatingHistories.AddRange(histories);
            dbContext.TeamRatings.AddRange(ratings.Select(x => new TeamRating
            {
                TeamId = x.Key,
                RulesetVersion = rulesetVersion,
                Elo = RoundRating(x.Value.Elo),
                GamesPlayed = x.Value.GamesPlayed,
                LastGameId = x.Value.LastGameId,
                UpdatedAtUtc = DateTime.UtcNow
            }));

            if (latestGameDate.HasValue)
            {
                var snapshotDate = DateOnly.FromDateTime(latestGameDate.Value);
                var position = 1;
                dbContext.RankingSnapshots.AddRange(ratings
                    .OrderByDescending(x => x.Value.Elo)
                    .ThenBy(x => x.Key)
                    .Select(x => new RankingSnapshot
                    {
                        Id = Guid.NewGuid(),
                        SnapshotDate = snapshotDate,
                        TeamId = x.Key,
                        RulesetVersion = rulesetVersion,
                        Elo = RoundRating(x.Value.Elo),
                        Position = position++,
                        CreatedAtUtc = DateTime.UtcNow
                    }));
            }

            run.Status = EloRebuildRunStatus.Completed;
            run.FinishedAtUtc = DateTime.UtcNow;
            run.FromGameDateTimeUtc = games.Count > 0 ? games[0].GameDateTimeUtc : null;
            run.GamesProcessed = games.Count;
            run.TeamsRated = ratings.Count;
            run.Notes = JsonSerializer.Serialize(new
            {
                baseRating = EloCalculator.BaseRating,
                kFactor = EloCalculator.KFactor,
                homeAdvantageElo = EloCalculator.HomeAdvantageElo,
                pointsPerEloMargin = rulesetVersion == EloRulesetVersions.PointMarginEloV1 ? EloCalculator.PointsPerEloMargin : (decimal?)null,
                competitionWeight = EloCalculator.CompetitionWeight
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await PublishNotificationAsync(run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            run = await dbContext.EloRebuildRuns.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
            run.Status = EloRebuildRunStatus.Pending;
            run.FinishedAtUtc = null;
            run.Notes = "Worker stopped during the rebuild; the run was returned to the queue.";
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ELO rebuild failed for ruleset {rulesetVersion}.", rulesetVersion);

            dbContext.ChangeTracker.Clear();
            run = await dbContext.EloRebuildRuns.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
            run.Status = EloRebuildRunStatus.Failed;
            run.FinishedAtUtc = DateTime.UtcNow;
            run.Notes = ex.Message;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            await PublishNotificationAsync(run, CancellationToken.None);
        }

        return new EloRebuildResult
        {
            RunId = run.Id,
            RulesetVersion = run.RulesetVersion,
            Status = run.Status,
            GamesProcessed = run.GamesProcessed,
            TeamsRated = run.TeamsRated,
            StartedAtUtc = run.StartedAtUtc,
            FinishedAtUtc = run.FinishedAtUtc,
            Notes = run.Notes
        };
    }

    private static RatingState GetRatingState(Dictionary<Guid, RatingState> ratings, Guid teamId)
    {
        if (ratings.TryGetValue(teamId, out var rating))
        {
            return rating;
        }

        rating = new RatingState(EloCalculator.BaseRating);
        ratings[teamId] = rating;
        return rating;
    }

    private static (int HomePosition, int AwayPosition) GetPositions(
        Dictionary<Guid, RatingState> ratings,
        Guid homeTeamId,
        Guid awayTeamId)
    {
        var position = 1;
        var homePosition = 0;
        var awayPosition = 0;

        foreach (var rating in ratings.OrderByDescending(x => x.Value.Elo).ThenBy(x => x.Key))
        {
            if (rating.Key == homeTeamId)
            {
                homePosition = position;
            }

            if (rating.Key == awayTeamId)
            {
                awayPosition = position;
            }

            position += 1;
        }

        return (homePosition, awayPosition);
    }

    private static decimal RoundRating(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundProbability(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal RoundMultiplier(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private Task PublishNotificationAsync(EloRebuildRun run, CancellationToken cancellationToken)
    {
        var notification = new EloRebuildRunNotification(
            run.Id,
            run.RulesetVersion,
            run.Status,
            DateTime.UtcNow);

        return notificationPublisher.PublishAsync(notification, cancellationToken);
    }

    private sealed record RatedGame(
        Guid Id,
        DateTime GameDateTimeUtc,
        Guid HomeTeamId,
        Guid AwayTeamId,
        short HomeScore,
        short AwayScore);

    private sealed class RatingState(decimal elo)
    {
        public decimal Elo { get; set; } = elo;
        public int GamesPlayed { get; set; }
        public Guid? LastGameId { get; set; }
    }
}
