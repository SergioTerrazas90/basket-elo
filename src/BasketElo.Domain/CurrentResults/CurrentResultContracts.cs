using BasketElo.Domain.Elo;

namespace BasketElo.Domain.CurrentResults;

public static class CurrentResultStatuses
{
    public const string Scheduled = "scheduled";
    public const string Live = "live";
    public const string Finished = "finished";
    public const string Postponed = "postponed";
    public const string Cancelled = "cancelled";
}

public static class CurrentResultReviewStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
}

public static class CurrentResultReviewReasons
{
    public const string UnknownCompetition = "unknown_competition";
    public const string UnsupportedCompetition = "unsupported_competition";
    public const string AmbiguousCompetition = "ambiguous_competition";
    public const string UnresolvedHomeTeam = "unresolved_home_team";
    public const string UnresolvedAwayTeam = "unresolved_away_team";
    public const string AmbiguousHomeTeam = "ambiguous_home_team";
    public const string AmbiguousAwayTeam = "ambiguous_away_team";
    public const string InvalidResult = "invalid_result";
    public const string AmbiguousPlannedFixture = "ambiguous_planned_fixture";
    public const string TournamentCycleConfirmationRequired = "tournament_cycle_confirmation_required";
}

public sealed record CurrentResultCandidate(
    string SourceGameId,
    string? SourceUrl,
    DateOnly SourceDate,
    DateTime GameDateTimeUtc,
    string CountryName,
    string CompetitionName,
    string? StageName,
    string HomeTeamName,
    string AwayTeamName,
    string HomeTeamSourceId,
    string AwayTeamSourceId,
    short? HomeScore,
    short? AwayScore,
    string Status,
    string RawStatus,
    string SourceRevision,
    string ParserVersion,
    string? SourceCompetitionId = null,
    bool? IsNeutralSite = null);

public sealed record CurrentResultFetchResult(
    DateOnly Date,
    string SourceUrl,
    string SourceRevision,
    IReadOnlyCollection<CurrentResultCandidate> Candidates);

public sealed record CurrentResultsRunSummary(
    Guid RunId,
    DateOnly FromDate,
    DateOnly ToDate,
    int PagesRead,
    int CandidatesRead,
    int GamesUpserted,
    int ReviewsOpened,
    int UnsupportedSkipped,
    int EloPoolsQueued,
    IReadOnlyCollection<string> DeferredEloPools,
    string Status,
    string? Error);

public sealed record CurrentResultReviewDto(
    Guid Id,
    string SourceGameId,
    string? SourceCompetitionId,
    DateOnly SourceDate,
    string? SourceUrl,
    string CountryName,
    string CompetitionName,
    string? StageName,
    string HomeTeamName,
    string AwayTeamName,
    DateTime GameDateTimeUtc,
    short? HomeScore,
    short? AwayScore,
    string ResultStatus,
    string Reason,
    string Status,
    string? SuggestedCompetitionName,
    string? SuggestedCompetitionCountryCode,
    Guid? TournamentCycleId,
    string? TournamentCycleKey,
    Guid? AssignedGameId,
    string? ResolutionAction,
    string? ResolutionNote,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CurrentResultsUnmatchedCompetitionDto(
    string Source,
    string? SourceCompetitionId,
    string CountryName,
    string CompetitionName,
    int ReviewCount,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc);

public sealed record CurrentResultsTournamentCycleOption(
    Guid Id,
    string Key,
    string Family,
    string EditionLabel,
    string DisplayName);

public sealed record CurrentResultsTournamentCycleOptionsResponse(
    IReadOnlyList<CurrentResultsTournamentCycleOption> Cycles,
    IReadOnlyList<string> Families);

public sealed record CreateCompetitionFromMergeRequest(
    string Name,
    string Type,
    string? CountryCode,
    string? EloPoolKey,
    int Tier,
    string SupportPolicy,
    string HomeAdvantagePolicy = HomeAdvantagePolicies.Automatic);

public sealed record MergeUnmatchedCompetitionRequest(
    string Source,
    string? SourceCompetitionId,
    string CountryName,
    string CompetitionName,
    Guid? TargetCompetitionId,
    CreateCompetitionFromMergeRequest? NewCompetition = null,
    Guid? TournamentCycleId = null,
    string? TournamentCycleFamily = null,
    string? TournamentCycleEditionLabel = null);

public sealed record IgnoreUnmatchedCompetitionRequest(
    string Source,
    string? SourceCompetitionId,
    string CountryName,
    string CompetitionName);

public sealed record CurrentResultReviewMatchDto(
    Guid GameId,
    string Source,
    string SourceGameId,
    DateTime GameDateTimeUtc,
    string CompetitionName,
    string Season,
    string? TournamentCycleKey,
    string HomeTeamName,
    string AwayTeamName,
    string Status);

public sealed record CurrentResultReviewResolutionRequest(
    string Action,
    Guid? GameId = null,
    string? Note = null);

public sealed record CurrentResultReviewResolutionDto(
    Guid ReviewId,
    string Status,
    Guid? AssignedGameId,
    int EloRunsQueued,
    string? Message);
