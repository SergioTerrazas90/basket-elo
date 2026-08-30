namespace BasketElo.Domain.Entities;

public class ModelLabRunEvolutionPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public Guid GameId { get; set; }
    public DateTime GameDateTimeUtc { get; set; }
    public string CompetitionName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public decimal Elo { get; set; }
    public decimal EloDelta { get; set; }
    public int Rank { get; set; }

    public ModelLabRun Run { get; set; } = null!;
    public ApplicationUser OwnerUser { get; set; } = null!;
    public Team Team { get; set; } = null!;
    public Game Game { get; set; } = null!;
}
