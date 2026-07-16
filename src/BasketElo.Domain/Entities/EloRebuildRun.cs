namespace BasketElo.Domain.Entities;

public class EloRebuildRun
{
    public Guid Id { get; set; }
    public DateTime QueuedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public DateTime? FromGameDateTimeUtc { get; set; }
    public string RulesetVersion { get; set; } = string.Empty;
    public string CompetitionName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int GamesProcessed { get; set; }
    public int TeamsRated { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
