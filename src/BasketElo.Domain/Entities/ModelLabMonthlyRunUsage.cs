namespace BasketElo.Domain.Entities;

public class ModelLabMonthlyRunUsage
{
    public Guid OwnerUserId { get; set; }
    public DateTime MonthStartUtc { get; set; }
    public int SlotNumber { get; set; }
    public Guid RunId { get; set; }
    public string UsageType { get; set; } = ModelLabRunUsageTypes.Run;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ApplicationUser OwnerUser { get; set; } = null!;
}

public static class ModelLabRunUsageTypes
{
    public const string Run = "run";
    public const string Retry = "retry";
}
