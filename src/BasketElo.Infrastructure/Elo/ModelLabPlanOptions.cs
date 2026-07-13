using BasketElo.Infrastructure.Identity;

namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabPlanOptions
{
    public const string SectionName = "ModelLab";

    public int FreeSavedModelLimit { get; set; } = 1;
    public int FreeStoredRunLimit { get; set; } = 1;
    public int PaidStoredRunLimit { get; set; } = 100;
    public string FreeLeagueName { get; set; } = "ACB";
    public string PaidEmails { get; set; } = string.Empty;

    public IReadOnlySet<string> GetNormalizedPaidEmails()
        => PaidEmails
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(AuthOptions.NormalizeEmail)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);
}
