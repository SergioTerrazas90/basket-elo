namespace BasketElo.Domain.Games;

public static class ManualGameStatuses
{
    public const string Scheduled = "scheduled";
    public const string Finished = "finished";
    public const string Postponed = "postponed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All =
        [Scheduled, Finished, Postponed, Cancelled];

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
}

public sealed record CreateManualGameRequest(
    Guid CompetitionId,
    string SeasonLabel,
    DateTime LocalDateTime,
    string TimeZoneId,
    Guid HomeTeamId,
    Guid AwayTeamId,
    short? HomeScore,
    short? AwayScore,
    string Status,
    string? CompetitionPhase = null,
    string? CompetitionRound = null,
    bool? IsNeutralSite = null);

public sealed record ManualGameCreatedResponse(
    Guid Id,
    string CompetitionName,
    string SeasonLabel,
    DateTime GameDateTimeUtc,
    string HomeTeamName,
    string AwayTeamName,
    short? HomeScore,
    short? AwayScore,
    string Status,
    bool EloEligible);
