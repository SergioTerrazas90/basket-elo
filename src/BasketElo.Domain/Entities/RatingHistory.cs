namespace BasketElo.Domain.Entities;

public class RatingHistory
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public Guid TeamId { get; set; }
    public Guid OpponentTeamId { get; set; }
    public DateTime GameDateTimeUtc { get; set; }
    public decimal PreElo { get; set; }
    public decimal PostElo { get; set; }
    public decimal EloDelta { get; set; }
    public int KFactorUsed { get; set; }
    public decimal ExpectedScore { get; set; }
    public decimal ActualScore { get; set; }
    public int GamesPlayedBefore { get; set; }
    public int? RatingPositionAfter { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Game Game { get; set; } = null!;
    public Team Team { get; set; } = null!;
    public Team OpponentTeam { get; set; } = null!;
}
