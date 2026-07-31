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
    public const string UnsupportedCompetition = "unsupported_competition";
    public const string AmbiguousCompetition = "ambiguous_competition";
    public const string UnresolvedHomeTeam = "unresolved_home_team";
    public const string UnresolvedAwayTeam = "unresolved_away_team";
    public const string AmbiguousHomeTeam = "ambiguous_home_team";
    public const string AmbiguousAwayTeam = "ambiguous_away_team";
    public const string InvalidResult = "invalid_result";
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
    string ParserVersion);

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
    int EloPoolsQueued,
    IReadOnlyCollection<string> DeferredEloPools,
    string Status,
    string? Error);

public sealed record CurrentResultReviewDto(
    Guid Id,
    string SourceGameId,
    DateOnly SourceDate,
    string? SourceUrl,
    string CountryName,
    string CompetitionName,
    string? StageName,
    string HomeTeamName,
    string AwayTeamName,
    string Reason,
    string Status,
    string? SuggestedCompetitionName,
    string? SuggestedCompetitionCountryCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
