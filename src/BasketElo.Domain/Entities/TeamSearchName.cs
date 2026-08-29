namespace BasketElo.Domain.Entities;

/// <summary>
/// A user-facing name that can be used to find a team. This is deliberately
/// separate from <see cref="TeamAlias"/>, whose values identify provider
/// records during ingestion.
/// </summary>
public class TeamSearchName
{
    public Guid Id { get; set; }
    public Guid TeamId { get; set; }
    public string Locale { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Team Team { get; set; } = null!;
}
