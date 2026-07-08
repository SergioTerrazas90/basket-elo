using BasketElo.Domain.Entities;

namespace BasketElo.Infrastructure.Identity;

public interface IIdentityHealthCheckService
{
    Task<IdentityHealthCheckRunDto> RunAsync(IdentityHealthCheckRequest request, CancellationToken cancellationToken);
    Task<IdentityHealthOptionsDto> GetOptionsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityHealthCheckRunDto>> GetRunsAsync(IdentityHealthCheckQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<IdentityHealthCheckFindingDto>> GetFindingsAsync(IdentityFindingQuery query, CancellationToken cancellationToken);
    Task<IdentityHealthCheckFindingDto> ResolveFindingAsync(Guid findingId, ResolveIdentityFindingRequest request, CancellationToken cancellationToken);
    Task DeleteRunAsync(Guid runId, CancellationToken cancellationToken);
    Task InvalidateChangedScopeAsync(IdentityChangedScope changedScope, CancellationToken cancellationToken);
}

public class IdentityHealthCheckRequest
{
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

public class IdentityChangedScope
{
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
