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

public sealed record CreateTeamAdminRequest(
    string CanonicalName,
    string? CountryCode = null,
    bool IsActive = true,
    string? Description = null);

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

public sealed record DuplicateTeamAliasGroupsResponse(
    int GroupCount,
    IReadOnlyList<DuplicateTeamAliasGroup> Groups);

public sealed record DuplicateTeamAliasGroup(
    string AliasName,
    string CountryCode,
    IReadOnlyList<DuplicateTeamAliasMember> Teams);

public sealed record DuplicateTeamAliasMember(
    Guid TeamId,
    string CanonicalName,
    IReadOnlyList<DuplicateTeamAliasDetail> Aliases);

public sealed record DuplicateTeamAliasDetail(
    Guid AliasId,
    string AliasName,
    string Source,
    string SourceTeamId);

public sealed record AcceptDuplicateTeamAliasGroupRequest(
    string AliasName,
    string CountryCode,
    IReadOnlyList<Guid> TeamIds,
    string? Note = null);
