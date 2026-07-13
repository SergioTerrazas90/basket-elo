namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabLimitException(
    string code,
    string message,
    bool upgradeRequired,
    int? savedModelLimit,
    string? allowedLeagueName,
    int? storedRunLimit = null) : Exception(message)
{
    public string Code { get; } = code;
    public bool UpgradeRequired { get; } = upgradeRequired;
    public int? SavedModelLimit { get; } = savedModelLimit;
    public int? StoredRunLimit { get; } = storedRunLimit;
    public string? AllowedLeagueName { get; } = allowedLeagueName;
}
