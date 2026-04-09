namespace BasketElo.Domain.Entities;

public class TeamAlias
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceTeamId { get; set; } = string.Empty;
    public string AliasName { get; set; } = string.Empty;
    public DateTime? ValidFromUtc { get; set; }
    public DateTime? ValidToUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Team Team { get; set; } = null!;
}
