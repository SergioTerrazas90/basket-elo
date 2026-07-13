namespace BasketElo.Domain.Entities;

public class ModelLabRunPeriodMetric
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid OwnerUserId { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public int Games { get; set; }
    public decimal WinnerAccuracy { get; set; }
    public decimal AverageMarginError { get; set; }

    public ModelLabRun Run { get; set; } = null!;
    public ApplicationUser OwnerUser { get; set; } = null!;
}
