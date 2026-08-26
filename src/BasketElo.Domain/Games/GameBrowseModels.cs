namespace BasketElo.Domain.Games;

public record GameBrowseResponse(
    IReadOnlyCollection<GameListItem> Games,
    GameFilterOptions Filters,
    GameBrowseSummary Summary,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public record GameListItem(
    Guid Id,
    string Source,
    string SourceGameId,
    string? SourceUrl,
    DateTime GameDateTimeUtc,
    string Country,
    string LeagueName,
    string Season,
    string? TournamentCycle,
    string? CompetitionPhase,
    string? CompetitionRound,
    string HomeTeam,
    string AwayTeam,
    short? HomeScore,
    short? AwayScore,
    string Status,
    bool EloEligible,
    string? EloExclusionReason,
    bool NeedsReview,
    IReadOnlyCollection<string> ReviewReasons,
    bool? IsNeutralSite = null,
    bool EffectiveNeutralSite = false);

public record GameFilterOptions(
    IReadOnlyCollection<string> Countries,
    IReadOnlyCollection<string> Leagues,
    IReadOnlyCollection<string> Seasons,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string> Sources,
    IReadOnlyCollection<TournamentCycleOption> TournamentCycles,
    IReadOnlyCollection<int> PlayedYears);

public record TournamentCycleOption(string Key, string DisplayName);

public record GameBrowseSummary(
    int TotalGames,
    int FilteredGames,
    int FinishedGames,
    int ScheduledGames,
    int ReviewGames,
    DateTime? FirstGameUtc,
    DateTime? LastGameUtc);

public record UpdateGameResultRequest(
    short? HomeScore,
    short? AwayScore,
    string Status = "finished");

public record UpdateGameSiteTreatmentRequest(bool? IsNeutralSite);
