namespace BasketElo.Domain.Entities;

public class Team
{
    public Guid Id { get; set; }
    public string CanonicalName { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
