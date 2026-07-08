namespace BasketElo.Domain.Entities;

public class IdentityReviewDecision
{
    public Guid Id { get; set; }
    public string DecisionKey { get; set; } = string.Empty;
    public string FindingType { get; set; } = string.Empty;
    public string ResolutionAction { get; set; } = string.Empty;
    public Guid? AffectedTeamId { get; set; }
    public Guid? RelatedTeamId { get; set; }
    public string? Source { get; set; }
    public string? SourceTeamId { get; set; }
    public string? RelatedSource { get; set; }
    public string? RelatedSourceTeamId { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Team? AffectedTeam { get; set; }
    public Team? RelatedTeam { get; set; }
}
