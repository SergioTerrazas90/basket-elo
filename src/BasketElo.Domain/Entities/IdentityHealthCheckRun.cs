namespace BasketElo.Domain.Entities;

public class IdentityHealthCheckRun
{
    public Guid Id { get; set; }
    public string? Source { get; set; }
    public string? Season { get; set; }
    public string? CountryCode { get; set; }
    public Guid? CompetitionId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string RulesVersion { get; set; } = IdentityHealthCheckRules.CurrentVersion;
    public string Status { get; set; } = IdentityHealthCheckStatus.Clean;
    public int FindingsCount { get; set; }
    public int UnresolvedBlockersCount { get; set; }
    public bool Forced { get; set; }
    public DateTime CheckedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InvalidatedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Competition? Competition { get; set; }
    public ICollection<IdentityHealthCheckFinding> Findings { get; set; } = new List<IdentityHealthCheckFinding>();
}

public static class IdentityHealthCheckStatus
{
    public const string Clean = "clean";
    public const string Warnings = "warnings";
    public const string Blockers = "blockers";
}

public static class IdentityHealthCheckRules
{
    public const string CurrentVersion = "identity-v1";
}
