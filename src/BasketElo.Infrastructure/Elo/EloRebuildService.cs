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
        var poolKey = EloPoolKeys.Normalize(run.EloPoolKey);
        var ruleset = EloCalculator.GetRulesetParameters(rulesetVersion);

        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var scopedGames = dbContext.Games
                .Where(x => x.Competition.EloPoolKey == poolKey);

            await DeleteExistingRatingsAsync(
                poolKey,
                rulesetVersion,
                cancellationToken);

            var games = await scopedGames
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
                    EloPoolKey = poolKey,
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
                    EloPoolKey = poolKey,
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
                EloPoolKey = poolKey,
                RulesetVersion = rulesetVersion,
                Elo = RoundRating(x.Value.Elo),
                GamesPlayed = x.Value.GamesPlayed,
                LastGameId = x.Value.LastGameId,
                UpdatedAtUtc = DateTime.UtcNow
            }));

            run.Status = EloRebuildRunStatus.Completed;
            run.FinishedAtUtc = DateTime.UtcNow;
            run.FromGameDateTimeUtc = games.Count > 0 ? games[0].GameDateTimeUtc : null;
            run.GamesProcessed = games.Count;
            run.TeamsRated = ratings.Count;
            run.Notes = JsonSerializer.Serialize(new
            {
                baseRating = ruleset.BaseRating,
                kFactor = ruleset.KFactor,
                homeAdvantageElo = ruleset.HomeAdvantageElo,
                pointsPerEloMargin = ruleset.PointsPerEloMargin,
                competitionWeight = ruleset.CompetitionWeight,
                poolKey,
                poolName = EloPoolKeys.DisplayName(poolKey),
                playoffPolicy = "Playoff and regular-season games use the current ruleset competition weight."
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            await PublishNotificationAsync(run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            run = await dbContext.EloRebuildRuns.SingleAsync(x => x.Id == run.Id, CancellationToken.None);
            run.Status = EloRebuildRunStatus.Pending;
            run.StartedAtUtc = null;
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
            EloPoolKey = poolKey,
            RulesetVersion = run.RulesetVersion,
            CompetitionName = run.CompetitionName,
            Status = run.Status,
            GamesProcessed = run.GamesProcessed,
            TeamsRated = run.TeamsRated,
            QueuedAtUtc = run.QueuedAtUtc,
            StartedAtUtc = run.StartedAtUtc,
            FinishedAtUtc = run.FinishedAtUtc,
            Notes = run.Notes
        };
    }

    private async Task DeleteExistingRatingsAsync(
        string poolKey,
        string rulesetVersion,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            await dbContext.RatingHistories
                .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.TeamRatings
                .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        dbContext.RatingHistories.RemoveRange(await dbContext.RatingHistories
            .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
            .ToListAsync(cancellationToken));
        dbContext.TeamRatings.RemoveRange(await dbContext.TeamRatings
            .Where(x => x.EloPoolKey == poolKey && x.RulesetVersion == rulesetVersion)
            .ToListAsync(cancellationToken));
        await dbContext.SaveChangesAsync(cancellationToken);
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
            run.EloPoolKey,
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
