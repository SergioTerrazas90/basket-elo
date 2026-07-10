namespace BasketElo.Domain.Entities;

public class BackfillInspectionDecision
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Status { get; set; } = BackfillInspectionStatus.ProviderGap;
    public string? Note { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class BackfillInspectionStatus
{
    public const string ConfirmedEmpty = "confirmed_empty";
    public const string ProviderGap = "provider_gap";
    public const string CovidPartialMissing = "covid_partial_missing";
    public const string Resolved = "resolved";
}
