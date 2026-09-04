namespace BasketElo.Infrastructure.Elo;

public sealed record ModelLabEntitlement(
    string PlanKey,
    bool CanSaveModels,
    bool IsPaid,
    int? SavedModelLimit,
    int? StoredRunLimit,
    int? MonthlyRunLimit,
    string? RequiredLeagueName,
    int? MinimumSeasonStartYear = null,
    DateTime? MonthlyRunWindowStartUtc = null,
    DateTime? MonthlyRunWindowEndUtc = null);
