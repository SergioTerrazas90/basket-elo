using BasketElo.Domain.Elo;

namespace BasketElo.Domain.Competitions;

public sealed record CompetitionAdminListResponse(
    IReadOnlyList<CompetitionAdminListItem> Competitions,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CompetitionAdminListItem(
    Guid Id,
    string Name,
    string Type,
    string? CountryCode,
    string? EloPoolKey,
    int Tier,
    bool IsActive,
    string SupportPolicy,
    string HomeAdvantagePolicy,
    int AliasCount,
    int GameCount,
    int OpenReviewCount);

public sealed record CompetitionAdminDetail(
    Guid Id,
    string Name,
    string Type,
    string? CountryCode,
    string? EloPoolKey,
    int Tier,
    bool IsActive,
    string SupportPolicy,
    string HomeAdvantagePolicy,
    DateTime CreatedAtUtc,
    int GameCount,
    int OpenReviewCount,
    IReadOnlyList<CompetitionAdminAlias> Aliases);

public sealed record CompetitionAdminAlias(
    Guid Id,
    string Source,
    string? SourceCompetitionId,
    string AliasName,
    DateTime CreatedAtUtc,
    int ReviewCount,
    int GameCount);

public sealed record CreateCompetitionAdminRequest(
    string Name,
    string Type,
    string? CountryCode,
    string? EloPoolKey,
    int Tier,
    bool IsActive,
    string SupportPolicy,
    string HomeAdvantagePolicy = HomeAdvantagePolicies.Automatic);

public sealed record UpdateCompetitionAdminRequest(
    string Name,
    string Type,
    string? CountryCode,
    string? EloPoolKey,
    int Tier,
    bool IsActive,
    string SupportPolicy,
    string HomeAdvantagePolicy = HomeAdvantagePolicies.Automatic);

public sealed record AddCompetitionAdminAliasRequest(
    string Source,
    string? SourceCompetitionId,
    string AliasName);

public sealed record CompetitionAdminOption(
    Guid Id,
    string Name,
    string? CountryCode,
    string SupportPolicy,
    string HomeAdvantagePolicy = HomeAdvantagePolicies.Automatic);
