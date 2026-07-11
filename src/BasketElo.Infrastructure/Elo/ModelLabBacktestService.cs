using BasketElo.Domain.Elo;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabBacktestService(BasketEloDbContext dbContext) : IModelLabBacktestService
{
    private const int MaxRatingsReturned = 100;
    private const int MaxMissesReturned = 12;

    public async Task<ModelLabOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var games = dbContext.Games.AsNoTracking();
        var defaults = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);

        return new ModelLabOptionsResponse(
            ToParameterSet(defaults),
            await games
                .Select(x => x.Competition.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken),
            await games
                .Select(x => x.Season.Label)
                .Distinct()
                .OrderByDescending(x => x)
                .ToListAsync(cancellationToken),
            await games.MinAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
            await games.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken));
    }

    public async Task<ModelLabBacktestResponse> RunAsync(ModelLabBacktestRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var parameters = ToRulesetParameters(request.Parameters);
        var baselineParameters = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);
        var fromUtc = Min(request.InitializationFromUtc, request.ScoredFromUtc);
        var toUtc = Max(request.InitializationToUtc, request.ScoredToUtc);

        var games = await dbContext.Games
            .AsNoTracking()
            .Where(x =>
                x.Competition.Name == request.LeagueName &&
                x.GameDateTimeUtc >= fromUtc &&
                x.GameDateTimeUtc <= toUtc &&
                x.HomeScore.HasValue &&
                x.AwayScore.HasValue &&
                x.HomeScore != x.AwayScore)
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Source)
            .ThenBy(x => x.SourceGameId)
            .ThenBy(x => x.Id)
            .Select(x => new BacktestGame(
                x.Id,
                x.GameDateTimeUtc,
                x.Season.Label,
                x.HomeTeamId,
                x.HomeTeam.CanonicalName,
                x.AwayTeamId,
                x.AwayTeam.CanonicalName,
                x.HomeScore!.Value,
                x.AwayScore!.Value))
            .ToListAsync(cancellationToken);

        var custom = RunSimulation(games, request, parameters);
        var baseline = RunSimulation(games, request, baselineParameters);

        return new ModelLabBacktestResponse(
            string.IsNullOrWhiteSpace(request.ModelName) ? "Untitled model" : request.ModelName.Trim(),
            request.LeagueName,
            request.Parameters,
            new ModelLabBacktestWindow(
                request.InitializationFromUtc,
                request.InitializationToUtc,
                custom.InitializationGames),
            new ModelLabBacktestWindow(
                request.ScoredFromUtc,
                request.ScoredToUtc,
                custom.ScoredPredictions.Count),
            custom.Summary,
            baseline.Summary,
            custom.Ratings,
            custom.ScoredPredictions
                .OrderByDescending(x => x.MarginError)
                .Take(MaxMissesReturned)
                .ToList(),
            custom.Periods);
    }

    private static SimulationResult RunSimulation(
        IReadOnlyCollection<BacktestGame> games,
        ModelLabBacktestRequest request,
        EloRulesetParameters parameters)
    {
        var ratings = new Dictionary<Guid, RatingState>();
        var predictions = new List<ModelLabPredictionRow>();
        var initializationGames = 0;

        foreach (var game in games)
        {
            var home = GetRatingState(ratings, game.HomeTeamId, game.HomeTeamName, parameters.BaseRating);
            var away = GetRatingState(ratings, game.AwayTeamId, game.AwayTeamName, parameters.BaseRating);
            var isInitialization = game.GameDateTimeUtc >= request.InitializationFromUtc &&
                game.GameDateTimeUtc <= request.InitializationToUtc;
            var isScored = game.GameDateTimeUtc >= request.ScoredFromUtc &&
                game.GameDateTimeUtc <= request.ScoredToUtc;

            if (!isInitialization && !isScored)
            {
                continue;
            }

            if (isScored)
            {
                var eloDiff = home.Elo + parameters.HomeAdvantageElo - away.Elo;
                var predictedHomeWinProbability = EloCalculator.CalculateExpectedResult(eloDiff);
                var predictedHomeMargin = parameters.PointsPerEloMargin.HasValue && parameters.PointsPerEloMargin.Value > 0
                    ? eloDiff / parameters.PointsPerEloMargin.Value
                    : 0m;
                var actualHomeMargin = game.HomeScore - game.AwayScore;
                var pickedHome = predictedHomeWinProbability >= 0.5m;
                var homeWon = game.HomeScore > game.AwayScore;

                predictions.Add(new ModelLabPredictionRow(
                    game.Id,
                    game.GameDateTimeUtc,
                    game.Season,
                    game.HomeTeamName,
                    game.AwayTeamName,
                    game.HomeScore,
                    game.AwayScore,
                    RoundProbability(predictedHomeWinProbability),
                    RoundRating(predictedHomeMargin),
                    actualHomeMargin,
                    RoundRating(Math.Abs(actualHomeMargin - predictedHomeMargin)),
                    pickedHome == homeWon));
            }
            else
            {
                initializationGames += 1;
            }

            var calculation = EloCalculator.Calculate(
                game.HomeScore,
                game.AwayScore,
                home.Elo,
                away.Elo,
                parameters);

            home.RecentDeltas.Enqueue(calculation.HomeDelta);
            away.RecentDeltas.Enqueue(-calculation.HomeDelta);
            TrimRecent(home);
            TrimRecent(away);
            home.Elo += calculation.HomeDelta;
            away.Elo -= calculation.HomeDelta;
            home.GamesPlayed += 1;
            away.GamesPlayed += 1;
        }

        return new SimulationResult(
            initializationGames,
            predictions,
            BuildSummary(predictions),
            BuildRatings(ratings),
            BuildPeriods(predictions));
    }

    private static ModelLabBacktestSummary BuildSummary(IReadOnlyCollection<ModelLabPredictionRow> predictions)
    {
        if (predictions.Count == 0)
        {
            return new ModelLabBacktestSummary(0, 0, 0m, 0m, 0m, 0m, 0m);
        }

        var correct = predictions.Count(x => x.PickedWinner);
        var brier = predictions.Average(x =>
        {
            var actual = x.HomeScore > x.AwayScore ? 1m : 0m;
            return Math.Pow((double)(x.PredictedHomeWinProbability - actual), 2d);
        });
        var logLoss = predictions.Average(x =>
        {
            var actual = x.HomeScore > x.AwayScore ? 1m : 0m;
            var probability = Math.Clamp(x.PredictedHomeWinProbability, 0.001m, 0.999m);
            return actual == 1m
                ? -Math.Log((double)probability)
                : -Math.Log((double)(1m - probability));
        });

        return new ModelLabBacktestSummary(
            predictions.Count,
            correct,
            RoundPercentage(correct / (decimal)predictions.Count),
            RoundProbability((decimal)brier),
            RoundProbability((decimal)logLoss),
            RoundRating(predictions.Average(x => x.MarginError)),
            RoundPercentage(predictions.Average(x => x.PredictedHomeWinProbability)));
    }

    private static IReadOnlyCollection<ModelLabRatingRow> BuildRatings(Dictionary<Guid, RatingState> ratings)
        => ratings
            .OrderByDescending(x => x.Value.Elo)
            .ThenBy(x => x.Value.TeamName)
            .Take(MaxRatingsReturned)
            .Select((x, index) => new ModelLabRatingRow(
                index + 1,
                x.Key,
                x.Value.TeamName,
                RoundRating(x.Value.Elo),
                x.Value.GamesPlayed,
                RoundRating(x.Value.RecentDeltas.Sum())))
            .ToList();

    private static IReadOnlyCollection<ModelLabPeriodMetric> BuildPeriods(IReadOnlyCollection<ModelLabPredictionRow> predictions)
        => predictions
            .GroupBy(x => new DateTime(x.GameDateTimeUtc.Year, x.GameDateTimeUtc.Month, 1))
            .OrderBy(x => x.Key)
            .Select(group =>
            {
                var rows = group.ToList();
                return new ModelLabPeriodMetric(
                    group.Key.ToString("yyyy-MM"),
                    rows.Count,
                    RoundPercentage(rows.Count(x => x.PickedWinner) / (decimal)rows.Count),
                    RoundRating(rows.Average(x => x.MarginError)));
            })
            .ToList();

    private static RatingState GetRatingState(
        Dictionary<Guid, RatingState> ratings,
        Guid teamId,
        string teamName,
        decimal baseRating)
    {
        if (ratings.TryGetValue(teamId, out var rating))
        {
            return rating;
        }

        rating = new RatingState(teamName, baseRating);
        ratings[teamId] = rating;
        return rating;
    }

    private static void TrimRecent(RatingState rating)
    {
        while (rating.RecentDeltas.Count > 5)
        {
            rating.RecentDeltas.Dequeue();
        }
    }

    private static void Validate(ModelLabBacktestRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LeagueName))
        {
            throw new ArgumentException("Choose a league before running a backtest.");
        }

        if (request.InitializationFromUtc > request.InitializationToUtc)
        {
            throw new ArgumentException("The initialization start date must be before its end date.");
        }

        if (request.ScoredFromUtc > request.ScoredToUtc)
        {
            throw new ArgumentException("The scored start date must be before its end date.");
        }

        if (request.Parameters.KFactor is < 1 or > 100)
        {
            throw new ArgumentException("K factor must be between 1 and 100.");
        }

        if (request.Parameters.BaseRating is < 500m or > 2500m)
        {
            throw new ArgumentException("Base rating must be between 500 and 2500.");
        }

        if (request.Parameters.HomeAdvantageElo is < -200m or > 300m)
        {
            throw new ArgumentException("Home advantage must be between -200 and 300 ELO.");
        }

        if (request.Parameters.CompetitionWeight is <= 0m or > 3m)
        {
            throw new ArgumentException("Competition weight must be greater than 0 and at most 3.");
        }

        if (request.Parameters.UsesMarginAdjustment &&
            (!request.Parameters.PointsPerEloMargin.HasValue || request.Parameters.PointsPerEloMargin.Value is < 5m or > 100m))
        {
            throw new ArgumentException("Point margin scale must be between 5 and 100 ELO per point when margin adjustment is on.");
        }
    }

    private static EloRulesetParameters ToRulesetParameters(ModelLabParameterSet parameters)
        => new(
            parameters.BaseRating,
            parameters.KFactor,
            parameters.HomeAdvantageElo,
            parameters.PointsPerEloMargin,
            parameters.CompetitionWeight,
            parameters.UsesMarginAdjustment);

    private static ModelLabParameterSet ToParameterSet(EloRulesetParameters parameters)
        => new(
            parameters.BaseRating,
            parameters.KFactor,
            parameters.HomeAdvantageElo,
            parameters.UsesMarginAdjustment,
            parameters.PointsPerEloMargin,
            parameters.CompetitionWeight);

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static decimal RoundRating(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundProbability(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal RoundPercentage(decimal value) => Math.Round(value * 100m, 1, MidpointRounding.AwayFromZero);

    private sealed record BacktestGame(
        Guid Id,
        DateTime GameDateTimeUtc,
        string Season,
        Guid HomeTeamId,
        string HomeTeamName,
        Guid AwayTeamId,
        string AwayTeamName,
        short HomeScore,
        short AwayScore);

    private sealed record SimulationResult(
        int InitializationGames,
        IReadOnlyCollection<ModelLabPredictionRow> ScoredPredictions,
        ModelLabBacktestSummary Summary,
        IReadOnlyCollection<ModelLabRatingRow> Ratings,
        IReadOnlyCollection<ModelLabPeriodMetric> Periods);

    private sealed class RatingState(string teamName, decimal elo)
    {
        public string TeamName { get; } = teamName;

        public decimal Elo { get; set; } = elo;

        public int GamesPlayed { get; set; }

        public Queue<decimal> RecentDeltas { get; } = new();
    }
}
