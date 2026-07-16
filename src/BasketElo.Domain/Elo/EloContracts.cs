namespace BasketElo.Domain.Elo;

public static class EloRulesetVersions
{
    public const string AdjustedV1 = "adjusted-v1";
    public const string BasicEloV1 = "basic-elo-v1";
    public const string PointMarginEloV1 = "point-margin-elo-v1";

    public static readonly IReadOnlyList<string> All = [AdjustedV1, BasicEloV1, PointMarginEloV1];
    public const string Default = AdjustedV1;
}

public static class EloRebuildRunStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

public static class EloRebuildNotifications
{
    public const string Channel = "elo_rebuild_events";
}

public sealed record EloRebuildRunNotification(
    Guid RunId,
    string? EloPoolKey,
    string RulesetVersion,
    string Status,
    DateTime OccurredAtUtc);

public sealed class EloRebuildRequest
{
    public string? RulesetVersion { get; set; }
    public string? PoolKey { get; set; }
    [Obsolete("Competition-scoped production rebuilds are replaced by PoolKey.")]
    public string? CompetitionName { get; set; }
}

public sealed class EloRebuildResult
{
    public Guid RunId { get; set; }
    public string EloPoolKey { get; set; } = string.Empty;
    public string RulesetVersion { get; set; } = string.Empty;
    public string CompetitionName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int GamesProcessed { get; set; }
    public int TeamsRated { get; set; }
    public DateTime QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public sealed record EloRulesetCatalogResponse(
    string DefaultRuleset,
    IReadOnlyList<string> Rulesets);

public sealed record EloRebuildRunDto(
    Guid Id,
    string? EloPoolKey,
    string RulesetVersion,
    string CompetitionName,
    string Status,
    int GamesProcessed,
    int TeamsRated,
    DateTime QueuedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    DateTime? FromGameDateTimeUtc,
    string? Notes);

public sealed record EloDashboardSummary(
    string EloPoolKey,
    string SelectedRuleset,
    int CompletedGames,
    int UnratedCompletedGames,
    int RatedTeams,
    DateTime? LatestCompletedGameUtc,
    DateTime? LatestSuccessfulRebuildUtc,
    DateTime? LatestRunQueuedAtUtc);

public sealed record EloDashboardResponse(
    EloRulesetCatalogResponse Rulesets,
    EloDashboardSummary Summary,
    IReadOnlyList<EloRebuildRunDto> RecentRuns);

public sealed record EloRankingsResponse(
    string EloPoolKey,
    string EloPoolName,
    string RulesetVersion,
    IReadOnlyCollection<EloRankingRow> Rankings,
    EloRankingFilterOptions Filters,
    EloRankingSummary Summary,
    EloRankingArchiveMetadata Archive,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record EloRankingRow(
    Guid TeamId,
    int Rank,
    int GlobalRank,
    string TeamName,
    string Country,
    decimal Elo,
    int GamesPlayed,
    decimal RecentMovement,
    DateTime? LastGameUtc);

public sealed record EloRankingFilterOptions(
    IReadOnlyCollection<string> Countries,
    IReadOnlyCollection<string> Competitions,
    IReadOnlyCollection<string> Seasons);

public sealed record EloRankingSummary(
    int RatedTeams,
    int FilteredTeams,
    DateTime? LatestGameUtc,
    string? TopTeamName,
    decimal? TopTeamElo,
    bool IsFiltered);

public sealed record EloRankingArchiveMetadata(
    string Mode,
    DateTime? RequestedAsOfUtc,
    DateTime? EffectiveAsOfUtc,
    string? EmptyReason);

public sealed record EloRankingsEvolutionResponse(
    string EloPoolKey,
    string RulesetVersion,
    IReadOnlyCollection<EloTeamEvolutionSeries> Series);

public sealed record EloMoversResponse(
    string EloPoolKey,
    string RulesetVersion,
    string Direction,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    IReadOnlyCollection<EloMoverRow> Movers,
    EloMoversSummary Summary,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record EloMoverRow(
    Guid TeamId,
    int Rank,
    string TeamName,
    string Country,
    decimal CurrentElo,
    decimal StartElo,
    decimal EndElo,
    decimal EloChange,
    int GamesInWindow,
    DateTime FirstGameUtc,
    DateTime LastGameUtc);

public sealed record EloMoversSummary(
    int TeamsWithMovement,
    int FilteredTeams,
    int WindowGames,
    bool IsFiltered);

public sealed record EloTeamEvolutionSeries(
    Guid TeamId,
    string TeamName,
    IReadOnlyCollection<EloTeamEvolutionPoint> Points);

public sealed record EloTeamEvolutionPoint(
    DateTime GameDateTimeUtc,
    decimal Elo,
    decimal? EloDelta = null,
    int? Rank = null);

public sealed record EloTeamDetailResponse(
    Guid TeamId,
    string TeamName,
    string Country,
    string EloPoolKey,
    string EloPoolName,
    string RulesetVersion,
    decimal Elo,
    int GlobalRank,
    int GamesPlayed,
    decimal RecentMovement,
    DateTime? LastGameUtc,
    IReadOnlyCollection<string> Competitions,
    IReadOnlyCollection<EloTeamGameDto> RecentGames,
    int RecentGamesPage,
    int RecentGamesPageSize,
    int RecentGamesTotalCount,
    int RecentGamesTotalPages,
    IReadOnlyCollection<EloTeamFormSummary> RecentForms,
    IReadOnlyCollection<EloRatingHistoryPoint> History);

public sealed record EloTeamGameDto(
    Guid GameId,
    DateTime GameDateTimeUtc,
    string Competition,
    string Season,
    string Opponent,
    bool WasHome,
    short? TeamScore,
    short? OpponentScore,
    decimal PreElo,
    decimal PostElo,
    decimal EloDelta);

public sealed record EloTeamFormSummary(
    int WindowGames,
    int GamesAvailable,
    int Wins,
    int Losses,
    decimal TotalEloChange,
    decimal AverageOpponentPreElo,
    EloTeamFormGame? BestWin,
    EloTeamFormGame? WorstLoss);

public sealed record EloTeamFormGame(
    Guid GameId,
    DateTime GameDateTimeUtc,
    string Opponent,
    bool WasHome,
    short? TeamScore,
    short? OpponentScore,
    decimal EloDelta,
    decimal OpponentPreElo);

public sealed record EloGameExplanationResponse(
    Guid GameId,
    string? EloPoolKey,
    string RulesetVersion,
    DateTime GameDateTimeUtc,
    string Competition,
    string Season,
    string HomeTeam,
    string AwayTeam,
    short? HomeScore,
    short? AwayScore,
    string Status,
    bool IsRated,
    string? UnavailableReason,
    EloGameTeamExplanation? Home,
    EloGameTeamExplanation? Away,
    EloGameRulesetExplanation Ruleset);

public sealed record EloGameTeamExplanation(
    Guid TeamId,
    string TeamName,
    bool WasHome,
    decimal PreElo,
    decimal PostElo,
    decimal EloDelta,
    decimal ExpectedScore,
    decimal ActualScore,
    int KFactorUsed,
    decimal MarginMultiplier,
    decimal CompetitionWeight,
    int GamesPlayedBefore,
    int? RatingPositionAfter);

public sealed record EloGameRulesetExplanation(
    decimal BaseRating,
    int KFactor,
    decimal HomeAdvantageElo,
    decimal? PointsPerEloMargin,
    decimal CompetitionWeight,
    bool UsesMarginAdjustment);

public sealed record EloRatingHistoryPoint(
    DateTime GameDateTimeUtc,
    decimal Elo,
    decimal EloDelta,
    int? Rank);

public sealed record EloPoolOption(
    string Key,
    string DisplayName,
    bool IsAvailable,
    int RatedTeams,
    DateTime? LatestRatedGameUtc,
    DateTime? LatestSuccessfulRebuildUtc);

public sealed record EloPoolCatalogResponse(
    string DefaultPool,
    IReadOnlyCollection<EloPoolOption> Pools);

public interface IEloRebuildService
{
    Task<EloRebuildResult> RebuildAsync(Guid runId, CancellationToken cancellationToken);
}
