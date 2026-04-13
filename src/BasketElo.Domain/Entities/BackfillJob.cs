namespace BasketElo.Domain.Entities;

public class BackfillJob
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public bool DryRun { get; set; } = true;
    public int MaxRequests { get; set; } = 2;
    public string Status { get; set; } = BackfillJobStatus.Pending;
    public int RequestsUsed { get; set; }
    public int WarningCount { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public string? SummaryJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class BackfillJobStatus
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string CompletedWithWarnings = "completed_with_warnings";
}
