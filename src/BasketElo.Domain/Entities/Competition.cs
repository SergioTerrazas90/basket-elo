namespace BasketElo.Domain.Entities;

public class Competition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public int Tier { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<CompetitionAlias> Aliases { get; set; } = new List<CompetitionAlias>();
}
