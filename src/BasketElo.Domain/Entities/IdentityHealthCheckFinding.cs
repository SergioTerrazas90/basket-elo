namespace BasketElo.Domain.Entities;

public class IdentityHealthCheckFinding
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string FindingType { get; set; } = string.Empty;
    public string Severity { get; set; } = IdentityFindingSeverity.Warning;
    public string Status { get; set; } = IdentityFindingStatus.Open;
    public string? Source { get; set; }
    public string? SourceTeamId { get; set; }
    public Guid? AffectedTeamId { get; set; }
    public string? RelatedSource { get; set; }
    public string? RelatedSourceTeamId { get; set; }
    public Guid? RelatedTeamId { get; set; }
    public string? Season { get; set; }
    public string? CountryCode { get; set; }
    public Guid? CompetitionId { get; set; }
    public string Evidence { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public string? ResolutionAction { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string? ResolutionNote { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public IdentityHealthCheckRun Run { get; set; } = null!;
    public Team? AffectedTeam { get; set; }
    public Team? RelatedTeam { get; set; }
    public Competition? Competition { get; set; }
}

public static class IdentityFindingSeverity
{
    public const string Warning = "warning";
    public const string Blocker = "blocker";
}

public static class IdentityFindingStatus
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string Ignored = "ignored";
}

public static class IdentityFindingType
{
    public const string AliasObservation = "alias_observation";
    public const string SourceTeamSplit = "source_team_split";
    public const string PossibleDuplicate = "possible_duplicate";
    public const string PossibleCrossSourceMatch = "possible_cross_source_match";
    public const string PossibleCrossSeasonSplit = "possible_cross_season_split";
    public const string MissingMetadata = "missing_metadata";
}
