namespace BasketElo.Domain.Teams;

public sealed record TeamAdminListResponse(
    IReadOnlyList<TeamAdminListItem> Teams,
    IReadOnlyList<TeamAdminCountryOption> Countries,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record TeamAdminOption(
    Guid Id,
    string CanonicalName,
    string CountryCode,
    bool IsActive);

public sealed record TeamAdminListItem(
    Guid Id,
    string CanonicalName,
    string CountryCode,
    bool IsActive,
    int AliasCount,
    int GameCount,
    int RatingHistoryCount,
    int RatingCount);

public sealed record TeamAdminCountryOption(
    string Code,
    string Name,
    int TeamCount);

public sealed record TeamAdminDetail(
    Guid Id,
    string CanonicalName,
    string? Description,
    string CountryCode,
    bool IsActive,
    DateTime CreatedAtUtc,
    int GameCount,
    int RatingHistoryCount,
    int RatingCount,
    IReadOnlyList<TeamAdminAlias> Aliases,
    TeamAdminOption? Predecessor,
    TeamAdminOption? Successor);

public sealed record TeamAdminAlias(
    Guid Id,
    string Source,
    string SourceTeamId,
    string AliasName,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc,
    DateTime CreatedAtUtc,
    int GameCount,
    int SeasonCount,
    DateTime? FirstUsedUtc,
    DateTime? LastUsedUtc);

public sealed record UpdateTeamAdminRequest(
    string CanonicalName,
    string CountryCode,
    bool IsActive,
    string? Description,
    Guid? PredecessorTeamId,
    Guid? SuccessorTeamId);

public sealed record AddTeamAdminAliasRequest(
    string Source,
    string SourceTeamId,
    string AliasName,
    DateTime? ValidFromUtc,
    DateTime? ValidToUtc);

public sealed record MergeTeamAdminRequest(
    Guid TargetTeamId,
    bool ConfirmMergeWithRatings);

public sealed record TeamAdminMergeResponse(
    Guid TargetTeamId,
    Guid RemovedTeamId,
    string TargetTeamName);

public sealed record TeamAdminExtractAliasResponse(
    Guid NewTeamId,
    string NewTeamName,
    string Source,
    string SourceTeamId,
    int ExtractedAliasCount);
