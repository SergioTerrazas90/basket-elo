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
        var leagueOptions = await GetLeagueOptionsAsync(cancellationToken);
        var seasons = await games
            .GroupBy(x => x.Season.Label)
            .Select(x => new
            {
                Label = x.Key,
                FirstGameUtc = x.Min(game => game.GameDateTimeUtc),
                LastGameUtc = x.Max(game => game.GameDateTimeUtc)
            })
            .OrderBy(x => x.FirstGameUtc)
            .ThenBy(x => x.Label)
            .ToListAsync(cancellationToken);

        return new ModelLabOptionsResponse(
            ToParameterSet(defaults),
            leagueOptions
                .Select(x => x.DisplayName)
                .OrderBy(x => x)
                .ToList(),
            leagueOptions
                .OrderBy(x => x.DisplayName)
                .Select(x => new ModelLabCompetitionOption(x.Id, x.Name, x.DisplayName, x.CountryCode))
                .ToList(),
            seasons
                .Select(x => new ModelLabSeasonOption(x.Label, x.FirstGameUtc, x.LastGameUtc))
                .ToList(),
            await games.MinAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken),
            await games.MaxAsync(x => (DateTime?)x.GameDateTimeUtc, cancellationToken));
    }

    public async Task<ModelLabBacktestResponse> RunAsync(ModelLabBacktestRequest request, CancellationToken cancellationToken)
        => (await RunDetailedAsync(request, cancellationToken)).Response;

    public async Task<ModelLabBacktestExecutionResult> RunDetailedAsync(
        ModelLabBacktestRequest request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var parameters = ToRulesetParameters(request.Parameters);
        var baselineParameters = EloCalculator.GetRulesetParameters(EloRulesetVersions.AdjustedV1);
        var fromUtc = Min(request.InitializationFromUtc, request.ScoredFromUtc);
        var toUtc = Max(request.InitializationToUtc, request.ScoredToUtc);
        var scope = await ResolveScopeAsync(request, cancellationToken);

        var gameQuery = dbContext.Games
            .AsNoTracking()
            .Where(x =>
                x.GameDateTimeUtc >= fromUtc &&
                x.GameDateTimeUtc <= toUtc &&
                x.EloEligible &&
                x.HomeScore.HasValue &&
                x.AwayScore.HasValue &&
                x.HomeScore != x.AwayScore);

        var scopeCompetitionIds = scope.Competitions.Select(x => x.Id).ToList();
        gameQuery = scopeCompetitionIds.Count > 0
            ? gameQuery.Where(x => scopeCompetitionIds.Contains(x.CompetitionId))
            : gameQuery.Where(x => x.Competition.Name == request.LeagueName);

        var games = await gameQuery
            .OrderBy(x => x.GameDateTimeUtc)
            .ThenBy(x => x.Source)
            .ThenBy(x => x.SourceGameId)
            .ThenBy(x => x.Id)
            .Select(x => new BacktestGame(
                x.Id,
                x.CompetitionId,
                x.Competition.Name,
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

        var response = new ModelLabBacktestResponse(
            string.IsNullOrWhiteSpace(request.ModelName) ? "Untitled model" : request.ModelName.Trim(),
            scope.DisplayName,
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
            custom.Ratings.Take(MaxRatingsReturned).ToList(),
            custom.ScoredPredictions
                .OrderByDescending(x => x.MarginError)
                .Take(MaxMissesReturned)
                .ToList(),
            custom.Periods);

        return new ModelLabBacktestExecutionResult(
            response,
            scope.Competitions,
            custom.ScoredPredictions,
            baseline.ScoredPredictions,
            custom.Ratings);
    }

    private async Task<IReadOnlyCollection<LeagueOption>> GetLeagueOptionsAsync(CancellationToken cancellationToken)
    {
        var competitions = await dbContext.Games
            .AsNoTracking()
            .Select(x => new CompetitionOption(
                x.CompetitionId,
                x.Competition.Name,
                x.Competition.CountryCode,
                x.Competition.EloPoolKey))
            .Distinct()
            .ToListAsync(cancellationToken);

        var duplicateNames = competitions
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return competitions
            .Select(x => new LeagueOption(
                x.Id,
                x.Name,
                duplicateNames.Contains(x.Name) ? $"{x.Name} ({FormatCountryCode(x.CountryCode)})" : x.Name,
                x.CountryCode,
                x.EloPoolKey))
            .ToList();
    }

    private async Task<ResolvedScope> ResolveScopeAsync(ModelLabBacktestRequest request, CancellationToken cancellationToken)
    {
        var leagueOptions = await GetLeagueOptionsAsync(cancellationToken);
        var scopeType = NormalizeScopeType(request.ScopeType);
        if (scopeType == ModelLabScopeTypes.AllCompetitions)
        {
            EnsureSinglePool(leagueOptions);
            return new ResolvedScope(
                "All competitions",
                leagueOptions
                    .Select(x => new ModelLabCompetitionOption(x.Id, x.Name, x.DisplayName, x.CountryCode))
                    .ToList());
        }

        if (scopeType == ModelLabScopeTypes.SelectedCompetitions)
        {
            var requestedIds = request.CompetitionIds?
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList() ?? [];

            if (requestedIds.Count == 0)
            {
                throw new ArgumentException("Choose at least one competition before running a selected-competition backtest.");
            }

            var validIds = leagueOptions.Select(x => x.Id).ToHashSet();
            var unknownIds = requestedIds.Where(x => !validIds.Contains(x)).ToList();
            if (unknownIds.Count > 0)
            {
                throw new ArgumentException("One or more selected competitions are not available for Model Lab.");
            }

            var selectedOptions = leagueOptions.Where(x => requestedIds.Contains(x.Id)).ToList();
            EnsureSinglePool(selectedOptions);

            return new ResolvedScope(
                requestedIds.Count == 1
                    ? leagueOptions.First(x => x.Id == requestedIds[0]).DisplayName
                    : $"{requestedIds.Count} competitions",
                selectedOptions
                    .Select(x => new ModelLabCompetitionOption(x.Id, x.Name, x.DisplayName, x.CountryCode))
                    .ToList());
        }

        var competitionId = ResolveCompetitionId(request.LeagueName, leagueOptions);
        return new ResolvedScope(
            request.LeagueName,
            competitionId.HasValue
                ? leagueOptions
                    .Where(x => x.Id == competitionId.Value)
                    .Select(x => new ModelLabCompetitionOption(x.Id, x.Name, x.DisplayName, x.CountryCode))
                    .ToList()
                : []);
    }

    private async Task<Guid?> ResolveCompetitionIdAsync(string leagueName, CancellationToken cancellationToken)
    {
        return ResolveCompetitionId(leagueName, await GetLeagueOptionsAsync(cancellationToken));
    }

    private static Guid? ResolveCompetitionId(string leagueName, IReadOnlyCollection<LeagueOption> leagueOptions)
    {
        var normalizedLeagueName = leagueName.Trim();
        var displayMatch = leagueOptions.FirstOrDefault(x =>
            string.Equals(x.DisplayName, normalizedLeagueName, StringComparison.OrdinalIgnoreCase));
        if (displayMatch is not null)
        {
            return displayMatch.Id;
        }

        var nameMatches = leagueOptions
            .Where(x => string.Equals(x.Name, normalizedLeagueName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return nameMatches.Count == 1 ? nameMatches[0].Id : null;
    }

    private static string NormalizeScopeType(string? scopeType)
    {
        return scopeType?.Trim().ToLowerInvariant() switch
        {
            ModelLabScopeTypes.SelectedCompetitions => ModelLabScopeTypes.SelectedCompetitions,
            ModelLabScopeTypes.AllCompetitions => ModelLabScopeTypes.AllCompetitions,
            _ => ModelLabScopeTypes.SingleCompetition
        };
    }

    private static void EnsureSinglePool(IReadOnlyCollection<LeagueOption> competitions)
    {
        var pools = competitions
            .Select(x => x.EloPoolKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (pools.Count > 1)
        {
            throw new ArgumentException(
                "Model Lab cannot mix NBA, Europe club, and national-team rating pools. Choose competitions from one ELO pool.");
        }
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
                var predictedHomeWinProbability = EloCalculator.CalculateExpectedResult(eloDiff, parameters.ProbabilityScale);
                var predictedHomeMargin = parameters.PointsPerEloMargin.HasValue && parameters.PointsPerEloMargin.Value > 0
                    ? eloDiff / parameters.PointsPerEloMargin.Value
                    : 0m;
                var actualHomeMargin = game.HomeScore - game.AwayScore;
                var pickedHome = predictedHomeWinProbability >= 0.5m;
                var homeWon = game.HomeScore > game.AwayScore;

                predictions.Add(new ModelLabPredictionRow(
                    game.Id,
                    game.CompetitionId,
                    game.CompetitionName,
                    game.GameDateTimeUtc,
                    game.Season,
                    game.HomeTeamId,
                    game.HomeTeamName,
                    game.AwayTeamId,
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

        ModelLabParameterValidator.Validate(request.Parameters);
    }

    private static EloRulesetParameters ToRulesetParameters(ModelLabParameterSet parameters)
        => new(
            parameters.BaseRating,
            parameters.KFactor,
            parameters.HomeAdvantageElo,
            parameters.PointsPerEloMargin,
            parameters.CompetitionWeight,
            parameters.UsesMarginAdjustment,
            parameters.ProbabilityScale,
            parameters.MarginDampenerFactor,
            parameters.MaxMarginMultiplier);

    private static ModelLabParameterSet ToParameterSet(EloRulesetParameters parameters)
        => new(
            parameters.BaseRating,
            parameters.KFactor,
            parameters.HomeAdvantageElo,
            parameters.ProbabilityScale,
            parameters.UsesMarginAdjustment,
            parameters.PointsPerEloMargin,
            parameters.CompetitionWeight,
            parameters.MarginDampenerFactor,
            parameters.MaxMarginMultiplier);

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static decimal RoundRating(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal RoundProbability(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static decimal RoundPercentage(decimal value) => Math.Round(value * 100m, 1, MidpointRounding.AwayFromZero);

    private static string FormatCountryCode(string? countryCode)
        => string.IsNullOrWhiteSpace(countryCode) ? "INT" : countryCode.Trim().ToUpperInvariant();

    private sealed record BacktestGame(
        Guid Id,
        Guid CompetitionId,
        string CompetitionName,
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

    private sealed record CompetitionOption(Guid Id, string Name, string? CountryCode, string? EloPoolKey);

    private sealed record LeagueOption(Guid Id, string Name, string DisplayName, string? CountryCode, string? EloPoolKey);

    private sealed record ResolvedScope(string DisplayName, IReadOnlyCollection<ModelLabCompetitionOption> Competitions);
}
