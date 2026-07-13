namespace BasketElo.Domain.Entities;

public class ModelLabRunScope
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid CompetitionId { get; set; }
    public string CompetitionName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }

    public ModelLabRun Run { get; set; } = null!;
    public Competition Competition { get; set; } = null!;
}
