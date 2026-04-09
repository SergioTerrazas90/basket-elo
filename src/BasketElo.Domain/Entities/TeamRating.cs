namespace BasketElo.Domain.Entities;

public class TeamRating
{
    public Guid TeamId { get; set; }
    public decimal Elo { get; set; } = 1500m;
    public int GamesPlayed { get; set; }
    public Guid? LastGameId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Team Team { get; set; } = null!;
    public Game? LastGame { get; set; }
}
