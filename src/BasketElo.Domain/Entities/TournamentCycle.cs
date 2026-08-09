namespace BasketElo.Domain.Entities;

public class TournamentCycle
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string EditionLabel { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Game> Games { get; set; } = new List<Game>();
    public ICollection<GameTournamentCycleLink> GameLinks { get; set; } = new List<GameTournamentCycleLink>();
}
