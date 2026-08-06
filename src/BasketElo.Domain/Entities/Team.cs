namespace BasketElo.Domain.Entities;

public class Team
{
    public Guid Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CountryCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? PredecessorTeamId { get; set; }
    public Team? PredecessorTeam { get; set; }
    public Guid? SuccessorTeamId { get; set; }
    public Team? SuccessorTeam { get; set; }

    public ICollection<TeamAlias> Aliases { get; set; } = new List<TeamAlias>();
}
