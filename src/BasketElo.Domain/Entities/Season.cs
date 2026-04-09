namespace BasketElo.Domain.Entities;

public class Season
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Competition Competition { get; set; } = null!;
}
