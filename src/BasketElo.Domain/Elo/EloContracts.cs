namespace BasketElo.Domain.Elo;

public static class EloRulesetVersions
{
    public const string BasicEloV1 = "basic-elo-v1";
    public const string PointMarginEloV1 = "point-margin-elo-v1";

    public static readonly IReadOnlyList<string> All = [BasicEloV1, PointMarginEloV1];
    public const string Default = PointMarginEloV1;
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
    string RulesetVersion,
    string Status,
    DateTime OccurredAtUtc);

public sealed class EloRebuildRequest
{
    public string? RulesetVersion { get; set; }
}

public sealed class EloRebuildResult
{
    public Guid RunId { get; set; }
    public string RulesetVersion { get; set; } = string.Empty;
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
    string RulesetVersion,
    string Status,
    int GamesProcessed,
    int TeamsRated,
    DateTime QueuedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    DateTime? FromGameDateTimeUtc,
    string? Notes);

public sealed record EloDashboardSummary(
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

public interface IEloRebuildService
{
    Task<EloRebuildResult> RebuildAsync(Guid runId, CancellationToken cancellationToken);
}
