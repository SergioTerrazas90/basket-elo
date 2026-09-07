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
    private const int RatingHistoryBatchSize = 2000;

    public async Task<EloRebuildResult> RebuildAsync(Guid runId, CancellationToken cancellationToken)
    {
        var results = await RebuildAsync([runId], cancellationToken);
        return results.Single();
    }

    public async Task<IReadOnlyList<EloRebuildResult>> RebuildAsync(
        IReadOnlyCollection<Guid> runIds,
        CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return [];
        }

        var requestedRunIds = runIds.Distinct().ToArray();
        var runs = await dbContext.EloRebuildRuns
            .Where(x => requestedRunIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (runs.Count != requestedRunIds.Length)
        {
            throw new InvalidOperationException("One or more requested ELO rebuild runs do not exist.");
        }

        if (runs.Any(x => x.Status != EloRebuildRunStatus.Running))
        {
            throw new InvalidOperationException("All ELO rebuild runs must be running.");
        }

        var poolKeys = runs.Select(x => EloPoolKeys.Normalize(x.EloPoolKey)).Distinct().ToArray();
        if (poolKeys.Length != 1)
        {
            throw new InvalidOperationException("A shared ELO rebuild can only contain runs from one pool.");
        }

        if (runs.Select(x => x.RulesetVersion).Distinct(StringComparer.OrdinalIgnoreCase).Count() != runs.Count)
        {
            throw new InvalidOperationException("A shared ELO rebuild cannot contain duplicate rulesets.");
        }

        var poolKey = poolKeys[0];
        try
        {
            var blockingGame = await dbContext.Games
                .AsNoTracking()
                .Where(x =>
                    x.Competition.EloPoolKey == poolKey &&
                    x.Status == "finished" &&
                    !x.EloEligible)
                .OrderBy(x => x.GameDateTimeUtc)
                .ThenBy(x => x.Source)
                .ThenBy(x => x.SourceGameId)
                .Select(x => new
                {
                    x.Source,
                    x.SourceGameId,
                    x.GameDateTimeUtc,
                    x.EloExclusionReason
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (blockingGame is not null)
            {
                var finishedAtUtc = DateTime.UtcNow;
                foreach (var run in runs)
                {
                    run.Status = EloRebuildRunStatus.Blocked;
                    run.FinishedAtUtc = finishedAtUtc;
                    run.Notes = $"ELO rebuild blocked by played non-Elo-eligible game {blockingGame.Source}:{blockingGame.SourceGameId} at {blockingGame.GameDateTimeUtc:O}; reason={blockingGame.EloExclusionReason ?? "unspecified"}. Resolve the game before rebuilding this pool.";
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                await PublishNotificationsAsync(runs, cancellationToken);
                return runs.Select(x => ToResult(x, poolKey)).ToList();
            }

            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;

            var games = await dbContext.Games
                .AsNoTracking()
                .Where(x =>
                    x.Competition.EloPoolKey == poolKey &&
                    x.EloEligible &&
                    x.HomeScore.HasValue &&
                    x.AwayScore.HasValue &&
                    x.HomeScore != x.AwayScore)
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
                    x.AwayScore!.Value,
                    x.IsNeutralSite,
                    x.Competition.Name,
                    x.Competition.Type,
                    x.Competition.HomeAdvantagePolicy,
                    x.CompetitionPhase,
                    x.CompetitionRound))
                .ToListAsync(cancellationToken);

            var states = runs.Select(run => new RebuildState(
                run,
                EloCalculator.GetRulesetParameters(run.RulesetVersion))).ToList();
            foreach (var state in states)
            {
                await DeleteExistingRatingsAsync(poolKey, state.Run.RulesetVersion, cancellationToken);
            }

            var historyBatch = new List<RatingHistory>(RatingHistoryBatchSize);
            foreach (var game in games)
            {
                foreach (var state in states)
                {
                    AddGameResult(state, game, poolKey, historyBatch);
                }

                if (historyBatch.Count >= RatingHistoryBatchSize)
                {
                    await SaveRatingHistoryBatchAsync(historyBatch, cancellationToken);
                }
            }

            await SaveRatingHistoryBatchAsync(historyBatch, cancellationToken);

            var updatedAtUtc = DateTime.UtcNow;
            foreach (var state in states)
            {
                dbContext.TeamRatings.AddRange(state.Ratings.Select(x => new TeamRating
                {
                    TeamId = x.Key,
                    EloPoolKey = poolKey,
                    RulesetVersion = state.Run.RulesetVersion,
                    Elo = RoundRating(x.Value.Elo),
                    GamesPlayed = x.Value.GamesPlayed,
                    LastGameId = x.Value.LastGameId,
                    UpdatedAtUtc = updatedAtUtc
                }));

                state.Run.Status = EloRebuildRunStatus.Completed;
                state.Run.FinishedAtUtc = updatedAtUtc;
                state.Run.FromGameDateTimeUtc = games.Count > 0 ? games[0].GameDateTimeUtc : null;
                state.Run.GamesProcessed = games.Count;
                state.Run.TeamsRated = state.Ratings.Count;
                state.Run.Notes = BuildNotes(state.Ruleset, poolKey);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            await PublishNotificationsAsync(runs, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            dbContext.ChangeTracker.Clear();
            runs = await dbContext.EloRebuildRuns
                .Where(x => requestedRunIds.Contains(x.Id))
                .ToListAsync(CancellationToken.None);
            foreach (var run in runs)
            {
                run.Status = EloRebuildRunStatus.Pending;
                run.HangfireJobId = null;
                run.StartedAtUtc = null;
                run.FinishedAtUtc = null;
                run.Notes = "Worker stopped during the rebuild; the run was returned to the queue.";
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Shared ELO rebuild failed for pool {poolKey}.", poolKey);

            dbContext.ChangeTracker.Clear();
            runs = await dbContext.EloRebuildRuns
                .Where(x => requestedRunIds.Contains(x.Id))
                .ToListAsync(CancellationToken.None);
            var finishedAtUtc = DateTime.UtcNow;
            foreach (var run in runs)
            {
                run.Status = EloRebuildRunStatus.Failed;
                run.FinishedAtUtc = finishedAtUtc;
                run.Notes = ex.Message;
            }

            await dbContext.SaveChangesAsync(CancellationToken.None);
            await PublishNotificationsAsync(runs, CancellationToken.None);
        }

        return runs.Select(x => ToResult(x, poolKey)).ToList();
    }

    private static void AddGameResult(
        RebuildState state,
        RatedGame game,
        string poolKey,
        List<RatingHistory> historyBatch)
    {
        var home = GetRatingState(state.Ratings, game.HomeTeamId, state.Ruleset.BaseRating);
        var away = GetRatingState(state.Ratings, game.AwayTeamId, state.Ruleset.BaseRating);
        var gameRuleset = HomeAdvantagePolicy.Apply(
            state.Ruleset,
            game.IsNeutralSite,
            game.CompetitionHomeAdvantagePolicy,
            game.CompetitionName,
            game.CompetitionType,
            game.CompetitionPhase,
            game.CompetitionRound);
        var calculation = EloCalculator.Calculate(
            game.HomeScore,
            game.AwayScore,
            home.Elo,
            away.Elo,
            gameRuleset);

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

        var positions = GetPositions(state.Ratings, game.HomeTeamId, game.AwayTeamId);
        var createdAtUtc = DateTime.UtcNow;
        historyBatch.Add(new RatingHistory
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            TeamId = game.HomeTeamId,
            OpponentTeamId = game.AwayTeamId,
            EloPoolKey = poolKey,
            RulesetVersion = state.Run.RulesetVersion,
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
            CreatedAtUtc = createdAtUtc
        });
        historyBatch.Add(new RatingHistory
        {
            Id = Guid.NewGuid(),
            GameId = game.Id,
            TeamId = game.AwayTeamId,
            OpponentTeamId = game.HomeTeamId,
            EloPoolKey = poolKey,
            RulesetVersion = state.Run.RulesetVersion,
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
            CreatedAtUtc = createdAtUtc
        });
    }

    private async Task SaveRatingHistoryBatchAsync(
        List<RatingHistory> historyBatch,
        CancellationToken cancellationToken)
    {
        if (historyBatch.Count == 0)
        {
            return;
        }

        dbContext.RatingHistories.AddRange(historyBatch);
        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var history in historyBatch)
        {
            dbContext.Entry(history).State = EntityState.Detached;
        }

        historyBatch.Clear();
    }

    private static string BuildNotes(EloRulesetParameters ruleset, string poolKey) =>
        JsonSerializer.Serialize(new
        {
            baseRating = ruleset.BaseRating,
            kFactor = ruleset.KFactor,
            homeAdvantageElo = ruleset.HomeAdvantageElo,
            pointsPerEloMargin = ruleset.PointsPerEloMargin,
            competitionWeight = ruleset.CompetitionWeight,
            poolKey,
            poolName = EloPoolKeys.DisplayName(poolKey),
            homeAdvantagePolicy = "Competition policy plus per-game neutral-site overrides; automatic mode recognizes Final Four and Final Eight metadata.",
            playoffPolicy = "Playoff and regular-season games use the current ruleset competition weight."
        });

    private static EloRebuildResult ToResult(EloRebuildRun run, string poolKey) => new()
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

    private static RatingState GetRatingState(
        Dictionary<Guid, RatingState> ratings,
        Guid teamId,
        decimal baseRating)
    {
        if (ratings.TryGetValue(teamId, out var rating))
        {
            return rating;
        }

        rating = new RatingState(baseRating);
        ratings[teamId] = rating;
        return rating;
    }

    private static (int HomePosition, int AwayPosition) GetPositions(
        Dictionary<Guid, RatingState> ratings,
        Guid homeTeamId,
        Guid awayTeamId)
    {
        var homeRating = ratings[homeTeamId].Elo;
        var awayRating = ratings[awayTeamId].Elo;
        var homePosition = 1;
        var awayPosition = 1;

        foreach (var rating in ratings)
        {
            if (rating.Value.Elo > homeRating ||
                (rating.Value.Elo == homeRating && rating.Key.CompareTo(homeTeamId) < 0))
            {
                homePosition += 1;
            }

            if (rating.Value.Elo > awayRating ||
                (rating.Value.Elo == awayRating && rating.Key.CompareTo(awayTeamId) < 0))
            {
                awayPosition += 1;
            }
        }

        return (homePosition, awayPosition);
    }

    private static decimal RoundRating(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal RoundProbability(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    private static decimal RoundMultiplier(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private async Task PublishNotificationsAsync(
        IEnumerable<EloRebuildRun> runs,
        CancellationToken cancellationToken)
    {
        foreach (var run in runs)
        {
            var notification = new EloRebuildRunNotification(
                run.Id,
                run.EloPoolKey,
                run.RulesetVersion,
                run.Status,
                DateTime.UtcNow);
            await notificationPublisher.PublishAsync(notification, cancellationToken);
        }
    }

    private sealed record RatedGame(
        Guid Id,
        DateTime GameDateTimeUtc,
        Guid HomeTeamId,
        Guid AwayTeamId,
        short HomeScore,
        short AwayScore,
        bool? IsNeutralSite,
        string CompetitionName,
        string CompetitionType,
        string CompetitionHomeAdvantagePolicy,
        string? CompetitionPhase,
        string? CompetitionRound);

    private sealed class RebuildState(EloRebuildRun run, EloRulesetParameters ruleset)
    {
        public EloRebuildRun Run { get; } = run;
        public EloRulesetParameters Ruleset { get; } = ruleset;
        public Dictionary<Guid, RatingState> Ratings { get; } = [];
    }

    private sealed class RatingState(decimal elo)
    {
        public decimal Elo { get; set; } = elo;
        public int GamesPlayed { get; set; }
        public Guid? LastGameId { get; set; }
    }
}
