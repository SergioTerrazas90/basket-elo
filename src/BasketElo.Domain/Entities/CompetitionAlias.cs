namespace BasketElo.Domain.Entities;

public class CompetitionAlias
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceCompetitionId { get; set; } = string.Empty;
    public string AliasName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Competition Competition { get; set; } = null!;
}
