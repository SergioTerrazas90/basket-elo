using BasketElo.Domain.Elo;

namespace BasketElo.Domain.Backfill;

public record ConfiguredBackfillLeague(
    string Provider,
    string Country,
    string LeagueName,
    string DisplayName,
    string StartSeason,
    IReadOnlyCollection<ConfiguredProviderLeague>? ProviderLeagues = null,
    string? CompetitionType = null,
    string? EndSeason = null,
    string EloPoolKey = EloPoolKeys.EuropeClubs,
    IReadOnlyCollection<string>? ExplicitSeasons = null,
    bool UsesSingleYearSeasonLabel = false);

public record ConfiguredProviderLeague(
    string Country,
    string LeagueName,
    string SeasonParameterFormat = "default");

public record BackfillCoverageRow(
    string Provider,
    string Country,
    string LeagueName,
    string DisplayName,
    string Season,
    string CoverageStatus,
    DateTime? LastRunUtc,
    int RequestsUsed,
    int WarningCount,
    IReadOnlyCollection<string> Warnings,
    bool DataPresent,
    int GameCount,
    string? LatestJobStatus,
    Guid? LatestJobId,
    bool NeedsInspection,
    IReadOnlyCollection<string> InspectionReasons,
    string InspectionSeverity,
    string? InspectionStatus,
    string? InspectionNote,
    DateTime? InspectionReviewedAtUtc);

public record BackfillCoverageResponse(IReadOnlyCollection<BackfillCoverageRow> Rows);

public record CreateBackfillJobRequest
{
    public string Provider { get; init; } = "api-sports";
    public string Country { get; init; } = "Spain";
    public string LeagueName { get; init; } = "ACB";
    public string Season { get; init; } = "2024-2025";
    public bool DryRun { get; init; } = true;
    public int MaxRequests { get; init; } = 2;
}

public record TriggerLeagueBackfillRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Provider { get; init; } = "api-sports";
    public string Country { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public bool DryRun { get; init; } = true;
    public int MaxRequests { get; init; } = 2;
}

public record TriggerLeagueRangeBackfillRequest
{
    public string DisplayName { get; init; } = string.Empty;
    public string Provider { get; init; } = "api-sports";
    public string Country { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string StartSeason { get; init; } = string.Empty;
    public string EndSeason { get; init; } = string.Empty;
    public bool OnlyMissing { get; init; } = true;
    public bool ReplaceExisting { get; init; }
    public bool NewestFirst { get; init; }
    public bool DryRun { get; init; } = true;
    public int MaxRequests { get; init; } = 2;
}

public record QueueBackfillJobsResponse(
    string Provider,
    string Country,
    string LeagueName,
    string StartSeason,
    string EndSeason,
    bool OnlyMissing,
    bool ReplaceExisting,
    bool NewestFirst,
    int RequestedSeasons,
    int QueuedJobs,
    int SkippedActiveJobs,
    int SkippedExistingSeasons);

public record NbaCurrentSeasonRefreshRequest
{
    public bool DryRun { get; init; }
    public int? MaxRequests { get; init; }
}

public record SaveBackfillInspectionDecisionRequest
{
    public string Provider { get; init; } = "api-sports";
    public string Country { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string Season { get; init; } = string.Empty;
    public string Status { get; init; } = "provider_gap";
    public string? Note { get; init; }
    public string? ReviewedBy { get; init; }
}
