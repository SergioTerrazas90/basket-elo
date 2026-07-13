namespace BasketElo.Domain.Entities;

public class ModelLabRunRating
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid OwnerUserId { get; set; }
    public int Rank { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public decimal Elo { get; set; }
    public int GamesPlayed { get; set; }
    public decimal RecentMovement { get; set; }

    public ModelLabRun Run { get; set; } = null!;
    public ApplicationUser OwnerUser { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
