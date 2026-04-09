namespace BasketElo.Domain.Entities;

public class RankingSnapshot
{
    public Guid Id { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public Guid TeamId { get; set; }
    public decimal Elo { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Team Team { get; set; } = null!;
}
