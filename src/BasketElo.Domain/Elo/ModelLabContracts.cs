namespace BasketElo.Domain.Elo;

public sealed record ModelLabOptionsResponse(
    ModelLabParameterSet Defaults,
    IReadOnlyCollection<string> Leagues,
    IReadOnlyCollection<ModelLabCompetitionOption> Competitions,
    IReadOnlyCollection<string> Seasons,
    DateTime? FirstGameUtc,
    DateTime? LastGameUtc);

public static class ModelLabScopeTypes
{
    public const string SingleCompetition = "single_competition";
    public const string SelectedCompetitions = "selected_competitions";
    public const string AllCompetitions = "all_competitions";
}

public sealed record ModelLabCompetitionOption(
    Guid Id,
    string Name,
    string DisplayName,
    string? CountryCode);

public sealed record ModelLabBacktestRequest(
    string? ModelName,
    ModelLabParameterSet Parameters,
    string LeagueName,
    DateTime InitializationFromUtc,
    DateTime InitializationToUtc,
    DateTime ScoredFromUtc,
    DateTime ScoredToUtc,
    string ScopeType = ModelLabScopeTypes.SingleCompetition,
    IReadOnlyCollection<Guid>? CompetitionIds = null);

public sealed record ModelLabParameterSet(
    decimal BaseRating,
    int KFactor,
    decimal HomeAdvantageElo,
    decimal ProbabilityScale,
    bool UsesMarginAdjustment,
    decimal? PointsPerEloMargin,
    decimal CompetitionWeight);

public sealed record ModelLabModelSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    string LeagueName,
    bool IsArchived,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ArchivedAtUtc,
    ModelLabModelVersionResponse CurrentVersion);

public sealed record ModelLabModelDetailResponse(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    string LeagueName,
    bool IsArchived,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ArchivedAtUtc,
    ModelLabModelVersionResponse CurrentVersion,
    IReadOnlyCollection<ModelLabModelVersionResponse> Versions);

public sealed record ModelLabModelVersionResponse(
    Guid Id,
    int VersionNumber,
    string ParameterSchemaVersion,
    ModelLabParameterSet Parameters,
    string? ExtensionDataJson,
    DateTime CreatedAtUtc);

public sealed record SaveModelLabModelRequest(
    string Name,
    string? Description,
    string LeagueName,
    ModelLabParameterSet Parameters,
    string? ExtensionDataJson);

public sealed record ArchiveModelLabModelRequest(bool IsArchived);

public sealed record ModelLabLimitErrorResponse(
    string Code,
    string Message,
    bool UpgradeRequired,
    int? SavedModelLimit,
    int? StoredRunLimit,
    string? AllowedLeagueName);

public sealed record ModelLabEntitlementResponse(
    string PlanKey,
    bool CanSaveModels,
    bool IsPaid,
    int? SavedModelLimit,
    int? StoredRunLimit,
    string? RequiredLeagueName);

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
    IReadOnlyCollection<ModelLabPeriodMetric> Periods,
    Guid? RunId = null);

public sealed record CreateModelLabRunRequest(
    Guid ModelId,
    Guid? ModelVersionId,
    DateTime InitializationFromUtc,
    DateTime InitializationToUtc,
    DateTime ScoredFromUtc,
    DateTime ScoredToUtc,
    string ScopeType = ModelLabScopeTypes.SingleCompetition,
    IReadOnlyCollection<Guid>? CompetitionIds = null);

public sealed record ModelLabRunCreateResponse(
    Guid RunId,
    Guid ModelId,
    Guid ModelVersionId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    ModelLabBacktestResponse Result);

public sealed record ModelLabRunSummaryResponse(
    Guid Id,
    Guid ModelId,
    Guid ModelVersionId,
    string ModelName,
    string LeagueName,
    string ScopeType,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    ModelLabBacktestWindow InitializationWindow,
    ModelLabBacktestWindow ScoredWindow,
    ModelLabBacktestSummary Summary,
    ModelLabBacktestSummary BaselineSummary);

public sealed record ModelLabRunQuotaResponse(
    int StoredRuns,
    int? StoredRunLimit,
    bool IsLimitReached);

public sealed record ModelLabRunDetailResponse(
    ModelLabRunSummaryResponse Run,
    IReadOnlyCollection<ModelLabCompetitionOption> Scopes,
    IReadOnlyCollection<ModelLabRatingRow> Ratings,
    IReadOnlyCollection<ModelLabPredictionRow> BiggestMisses,
    IReadOnlyCollection<ModelLabPeriodMetric> Periods);

public sealed record ModelLabRunPredictionPageResponse(
    Guid RunId,
    int Total,
    int Skip,
    int Take,
    IReadOnlyCollection<ModelLabPredictionRow> Items);

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
    Guid CompetitionId,
    string CompetitionName,
    DateTime GameDateTimeUtc,
    string Season,
    Guid HomeTeamId,
    string HomeTeam,
    Guid AwayTeamId,
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
