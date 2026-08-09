namespace BasketElo.Domain.Entities;

/// <summary>
/// Associates one canonical game with an additional tournament cycle without
/// duplicating the game row. Historical World Cup qualification used existing
/// continental/Olympic tournaments as the qualifying route.
/// </summary>
public class GameTournamentCycleLink
{
    public Guid GameId { get; set; }
    public Guid TournamentCycleId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;

    public Game Game { get; set; } = null!;
    public TournamentCycle TournamentCycle { get; set; } = null!;
}
