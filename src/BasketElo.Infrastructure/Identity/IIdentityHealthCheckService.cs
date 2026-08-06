using BasketElo.Domain.Entities;

namespace BasketElo.Infrastructure.Identity;

public interface IIdentityHealthCheckService
{
    Task<IdentityHealthCheckRunDto> RunAsync(IdentityHealthCheckRequest request, CancellationToken cancellationToken);
    Task<IdentityHealthOptionsDto> GetOptionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityHealthCheckRunDto>> GetRunsAsync(IdentityHealthCheckQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityHealthCheckFindingDto>> GetFindingsAsync(IdentityFindingQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityReviewCandidateDto>> GetReviewCandidatesAsync(IdentityReviewQuery query, CancellationToken cancellationToken);
    Task<IdentityReviewCandidateDto> ResolveReviewCandidateAsync(ResolveIdentityPairRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityDistinctTeamsDecisionDto>> GetDistinctTeamDecisionsAsync(CancellationToken cancellationToken);
    Task<IdentityEvidenceGamesResponseDto> GetEvidenceGamesAsync(Guid findingId, int limit, CancellationToken cancellationToken);
    Task<IdentityTeamMergeResultDto> MergeTeamsAsync(Guid sourceTeamId, Guid targetTeamId, bool confirmMergeWithRatings, CancellationToken cancellationToken);
    Task<IdentityHealthCheckFindingDto> ResolveFindingAsync(Guid findingId, ResolveIdentityFindingRequest request, CancellationToken cancellationToken);
    Task RemoveDistinctTeamDecisionAsync(Guid leftTeamId, Guid rightTeamId, CancellationToken cancellationToken);
    Task DeleteRunAsync(Guid runId, CancellationToken cancellationToken);
    Task InvalidateChangedScopeAsync(IdentityChangedScope changedScope, CancellationToken cancellationToken);
}

public class IdentityHealthCheckRequest
{
    public string? EloPoolKey { get; set; }
    public string? Source { get; set; }
    public string? Season { get; set; }
    public string? CountryCode { get; set; }
    public Guid? CompetitionId { get; set; }
    public bool Force { get; set; }
}

public class IdentityHealthCheckQuery
{
    public string? Source { get; set; }
    public string? Season { get; set; }
    public string? CountryCode { get; set; }
    public Guid? CompetitionId { get; set; }
    public int Limit { get; set; } = 25;
}

public class IdentityFindingQuery
{
    public Guid? RunId { get; set; }
    public string? Status { get; set; }
    public string? Severity { get; set; }
    public string? Source { get; set; }
    public string? Season { get; set; }
    public string? CountryCode { get; set; }
    public Guid? CompetitionId { get; set; }
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 100;
}

public class IdentityReviewQuery
{
    public Guid? RunId { get; set; }
    public string? CountryCode { get; set; }
    public string? TeamCountryCode { get; set; }
    public string? Status { get; set; } = "open";
    public int Limit { get; set; } = 250;
}

public class ResolveIdentityFindingRequest
{
    public string Action { get; set; } = "resolve";
    public Guid? TargetTeamId { get; set; }
    public string? CanonicalName { get; set; }
    public string? CountryCode { get; set; }
    public bool? IsActive { get; set; }
    public bool ConfirmMergeWithRatings { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Note { get; set; }
}

public class ResolveIdentityPairRequest
{
    public Guid RunId { get; set; }
    public Guid LeftTeamId { get; set; }
    public Guid RightTeamId { get; set; }
    public string Action { get; set; } = "defer_review";
    public Guid? TargetTeamId { get; set; }
    public bool ConfirmMergeWithRatings { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Note { get; set; }
}

public class IdentityChangedScope
{
    public string? EloPoolKey { get; set; }
    public string? Source { get; set; }
    public string? Season { get; set; }
    public string? CountryCode { get; set; }
    public Guid? CompetitionId { get; set; }
}

public sealed record IdentityHealthOptionsDto(
    IReadOnlyList<string> Sources,
    IReadOnlyList<string> Seasons,
    IReadOnlyList<IdentityCountryOptionDto> Countries,
    IReadOnlyList<IdentityCompetitionOptionDto> Competitions);

public sealed record IdentityCountryOptionDto(
    string Code,
    string Name);

public sealed record IdentityCompetitionOptionDto(
    Guid Id,
    string Name,
    string? CountryCode);

public sealed record IdentityHealthCheckRunDto(
    Guid Id,
    string? Source,
    string? Season,
    string? CountryCode,
    Guid? CompetitionId,
    string ScopeKey,
    string RulesVersion,
    string Status,
    int FindingsCount,
    int UnresolvedBlockersCount,
    int OpenFindingsCount,
    int OpenWarningsCount,
    int OpenBlockersCount,
    int ResolvedFindingsCount,
    int IgnoredFindingsCount,
    IReadOnlyList<IdentityFindingTypeSummaryDto> TypeSummaries,
    bool Forced,
    DateTime CheckedAtUtc,
    DateTime? InvalidatedAtUtc);

public sealed record IdentityFindingTypeSummaryDto(
    string FindingType,
    int OpenCount,
    int ResolvedCount,
    int IgnoredCount);

public sealed record IdentityHealthCheckFindingDto(
    Guid Id,
    Guid RunId,
    string FindingType,
    string Severity,
    string Status,
    string? Source,
    string? SourceTeamId,
    Guid? AffectedTeamId,
    string? AffectedTeamName,
    string? AffectedTeamCountryCode,
    bool? AffectedTeamIsActive,
    string? RelatedSource,
    string? RelatedSourceTeamId,
    Guid? RelatedTeamId,
    string? RelatedTeamName,
    string? Season,
    string? CountryCode,
    Guid? CompetitionId,
    string? SuggestedCountryCode,
    string Evidence,
    string SuggestedAction,
    string? ResolutionAction,
    string? ResolutionNote,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

public sealed record IdentityEvidenceGameDto(
    Guid Id,
    string Source,
    string SourceGameId,
    string? SourceUrl,
    DateTime GameDateTimeUtc,
    string? CountryCode,
    string CompetitionName,
    string Season,
    string HomeTeamName,
    string AwayTeamName,
    short? HomeScore,
    short? AwayScore,
    string Status);

public sealed record IdentityEvidenceGamesResponseDto(
    string FindingType,
    IdentityEvidenceTeamGamesDto? AffectedTeam,
    IdentityEvidenceTeamGamesDto? RelatedTeam);

public sealed record IdentityEvidenceTeamGamesDto(
    Guid TeamId,
    string DisplayName,
    string? Source,
    string? SourceTeamId,
    IReadOnlyList<IdentityEvidenceGameDto> Games);

public sealed record IdentityTeamMergeResultDto(
    Guid TargetTeamId,
    Guid RemovedTeamId,
    string TargetTeamName);

public sealed record IdentityDistinctTeamsDecisionDto(
    Guid LeftTeamId,
    string LeftTeamName,
    Guid RightTeamId,
    string RightTeamName,
    string? Note,
    string? CreatedBy,
    DateTime CreatedAtUtc);

public sealed record IdentityReviewTeamDto(
    Guid Id,
    string Name,
    string? CountryCode,
    bool IsActive,
    int GameCount,
    int AliasCount,
    DateTime? LastGameUtc);

public sealed record IdentityReviewCandidateDto(
    Guid RunId,
    string Status,
    string Severity,
    IdentityReviewTeamDto LeftTeam,
    IdentityReviewTeamDto RightTeam,
    IReadOnlyList<string> FindingTypes,
    int TotalFindingCount,
    int OpenFindingCount,
    Guid PrimaryFindingId,
    IReadOnlyList<Guid> FindingIds,
    IReadOnlyList<string> Evidence);
