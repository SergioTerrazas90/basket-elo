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
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

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
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? Notes { get; set; }
}

public interface IEloRebuildService
{
    Task<IReadOnlyList<EloRebuildResult>> RebuildAsync(string? rulesetVersion, CancellationToken cancellationToken);
}
