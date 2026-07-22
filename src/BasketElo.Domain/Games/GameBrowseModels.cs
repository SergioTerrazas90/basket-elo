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
    string HomeTeam,
    string AwayTeam,
    short? HomeScore,
    short? AwayScore,
    string Status,
    bool EloEligible,
    string? EloExclusionReason,
    bool NeedsReview,
    IReadOnlyCollection<string> ReviewReasons);

public record GameFilterOptions(
    IReadOnlyCollection<string> Countries,
    IReadOnlyCollection<string> Leagues,
    IReadOnlyCollection<string> Seasons,
    IReadOnlyCollection<string> Statuses,
    IReadOnlyCollection<string> Sources);

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
