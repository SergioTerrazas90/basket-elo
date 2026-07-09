using BasketElo.Domain.Elo;

namespace BasketElo.Domain.Admin;

public sealed record AdminDashboardResponse(
    EloDashboardResponse Elo,
    AdminSystemStatus System,
    IReadOnlyCollection<AdminBackfillJobRow> RecentBackfillJobs,
    IReadOnlyCollection<AdminGameCoverageRow> GameCoverage,
    AdminDataHealthSummary DataHealth);

public sealed record AdminSystemStatus(
    bool DatabaseCanConnect,
    int PendingMigrationsCount,
    DateTime ServerUtc,
    string WorkerStatus,
    DateTime? LatestWorkerActivityUtc);

public sealed record AdminBackfillJobRow(
    Guid Id,
    string Provider,
    string Country,
    string LeagueName,
    string Season,
    bool DryRun,
    string Status,
    int RequestsUsed,
    int WarningCount,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? SummaryJson,
    string? ErrorMessage);

public sealed record AdminGameCoverageRow(
    string Competition,
    string Season,
    string CountryCode,
    int GamesLoaded,
    int CompletedGames,
    int UnratedCompletedGames,
    DateTime? LatestGameUtc);

public sealed record AdminDataHealthSummary(
    int CompletedGames,
    int UnratedCompletedGames,
    int TeamsMissingCountry,
    int PossibleDuplicateAliasGroups,
    int OpenIdentityWarnings,
    int OpenIdentityBlockers,
    DateTime? LatestSuccessfulRebuildUtc);
