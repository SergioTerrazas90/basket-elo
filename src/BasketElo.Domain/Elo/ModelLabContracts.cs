namespace BasketElo.Domain.Elo;

public sealed record ModelLabOptionsResponse(
    ModelLabParameterSet Defaults,
    IReadOnlyCollection<string> Leagues,
    IReadOnlyCollection<string> Seasons,
    DateTime? FirstGameUtc,
    DateTime? LastGameUtc);

public sealed record ModelLabBacktestRequest(
    string? ModelName,
    ModelLabParameterSet Parameters,
    string LeagueName,
    DateTime InitializationFromUtc,
    DateTime InitializationToUtc,
    DateTime ScoredFromUtc,
    DateTime ScoredToUtc);

public sealed record ModelLabParameterSet(
    decimal BaseRating,
    int KFactor,
    decimal HomeAdvantageElo,
    bool UsesMarginAdjustment,
    decimal? PointsPerEloMargin,
    decimal CompetitionWeight);

public sealed record ModelLabBacktestResponse(
    string ModelName,
    string LeagueName,
    ModelLabParameterSet Parameters,
    ModelLabBacktestWindow InitializationWindow,
    ModelLabBacktestWindow ScoredWindow,
    ModelLabBacktestSummary Summary,
    ModelLabBacktestSummary BaselineSummary,
    IReadOnlyCollection<ModelLabRatingRow> Ratings,
    IReadOnlyCollection<ModelLabPredictionRow> BiggestMisses,
    IReadOnlyCollection<ModelLabPeriodMetric> Periods);

public sealed record ModelLabBacktestWindow(
    DateTime FromUtc,
    DateTime ToUtc,
    int Games);

public sealed record ModelLabBacktestSummary(
    int ScoredGames,
    int CorrectWinners,
    decimal WinnerAccuracy,
    decimal BrierScore,
    decimal LogLoss,
    decimal AverageMarginError,
    decimal AveragePredictedHomeWinProbability);

public sealed record ModelLabRatingRow(
    int Rank,
    Guid TeamId,
    string TeamName,
    decimal Elo,
    int GamesPlayed,
    decimal RecentMovement);

public sealed record ModelLabPredictionRow(
    Guid GameId,
    DateTime GameDateTimeUtc,
    string Season,
    string HomeTeam,
    string AwayTeam,
    short HomeScore,
    short AwayScore,
    decimal PredictedHomeWinProbability,
    decimal PredictedHomeMargin,
    decimal ActualHomeMargin,
    decimal MarginError,
    bool PickedWinner);

public sealed record ModelLabPeriodMetric(
    string Label,
    int Games,
    decimal WinnerAccuracy,
    decimal AverageMarginError);
