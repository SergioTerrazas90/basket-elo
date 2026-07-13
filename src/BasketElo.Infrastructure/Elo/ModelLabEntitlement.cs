namespace BasketElo.Infrastructure.Elo;

public sealed record ModelLabEntitlement(
    string PlanKey,
    bool CanSaveModels,
    bool IsPaid,
    int? SavedModelLimit,
    int? StoredRunLimit,
    string? RequiredLeagueName);
