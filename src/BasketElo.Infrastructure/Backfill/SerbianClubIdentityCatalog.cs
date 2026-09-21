namespace BasketElo.Infrastructure.Backfill;

/// <summary>
/// Curated source identities for Serbian clubs whose provider ids span two
/// real-world clubs. API-Sports id 1066 uses "FMP Beograd" for both the
/// original FMP Zeleznik and the modern club descended from Radnicki Novi Sad.
/// </summary>
public static class SerbianClubIdentityCatalog
{
    public const string OriginalFmpCanonicalName = "FMP Železnik";
    public const string ModernFmpCanonicalName = "FMP";
    public const string CountryCode = "RS";

    public static SerbianClubIdentity? Resolve(
        string source,
        string sourceTeamId,
        string season)
    {
        if (!string.Equals(source, ApiSportsBasketballDataProvider.Source, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(sourceTeamId.Trim(), "1066", StringComparison.OrdinalIgnoreCase) ||
            !SeasonLabelNormalizer.TryParseSeason(season, out _, out var startYear))
        {
            return null;
        }

        return startYear switch
        {
            <= 2010 => new SerbianClubIdentity(OriginalFmpCanonicalName, CountryCode, IsActive: false),
            >= 2013 => new SerbianClubIdentity(ModernFmpCanonicalName, CountryCode, IsActive: true),
            _ => null
        };
    }
}

public sealed record SerbianClubIdentity(
    string CanonicalName,
    string CountryCode,
    bool IsActive);
