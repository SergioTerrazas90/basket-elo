namespace BasketElo.Domain.Entities;

public class Game
{
    public Guid Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string SourceGameId { get; set; } = string.Empty;
    public Guid CompetitionId { get; set; }
    public Guid SeasonId { get; set; }
    public DateTime GameDateTimeUtc { get; set; }
    public Guid HomeTeamId { get; set; }
    public Guid AwayTeamId { get; set; }
    public short? HomeScore { get; set; }
    public short? AwayScore { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime IngestedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public Competition Competition { get; set; } = null!;
    public Season Season { get; set; } = null!;
    public Team HomeTeam { get; set; } = null!;
    public Team AwayTeam { get; set; } = null!;
}
