namespace BasketElo.Domain.Entities;

public class ModelLabModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string LeagueName { get; set; } = "ACB";
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAtUtc { get; set; }

    public ApplicationUser OwnerUser { get; set; } = null!;
    public ICollection<ModelLabModelVersion> Versions { get; set; } = new List<ModelLabModelVersion>();
}
