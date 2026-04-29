namespace BasketElo.Domain.Backfill;

public record ConfiguredBackfillLeague(
    string Provider,
    string Country,
    string LeagueName,
    string DisplayName,
    string StartSeason);

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
    bool DataPresent,
    int GameCount,
    string? LatestJobStatus,
    Guid? LatestJobId);

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
    public string Provider { get; init; } = "api-sports";
    public string Country { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public bool DryRun { get; init; } = true;
    public int MaxRequests { get; init; } = 2;
}
