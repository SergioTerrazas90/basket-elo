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

public sealed record AdminAttentionSummary(
    int OpenResultReviews,
    int OpenIdentityWarnings,
    int OpenIdentityBlockers,
    int PossibleDuplicateAliasGroups,
    int TeamsMissingCountry,
    int UnratedEligibleGames);

public sealed record AdminSocialPostsResponse(
    DateOnly RequestedDate,
    DateOnly ContentDate,
    bool UsedFallbackDate,
    DateTime GeneratedAtUtc,
    int RatedGames,
    IReadOnlyCollection<AdminSocialPostDraft> Posts);

public sealed record AdminSocialPostDraft(
    string Id,
    string Type,
    string League,
    string Kicker,
    string Headline,
    string Subheadline,
    string CaptionEnglish,
    string CaptionSpanish,
    string LinkPath,
    AdminSocialGameVisual? Game,
    IReadOnlyCollection<AdminSocialRankingEntry> Rankings);

public sealed record AdminSocialGameVisual(
    Guid GameId,
    string Winner,
    string Loser,
    short WinnerScore,
    short LoserScore,
    decimal WinnerPreElo,
    decimal WinnerPostElo,
    decimal OpponentPreElo,
    decimal ExpectedWinPercentage,
    decimal EloDelta);

public sealed record AdminSocialRankingEntry(
    int Rank,
    Guid TeamId,
    string TeamName,
    decimal Elo);
