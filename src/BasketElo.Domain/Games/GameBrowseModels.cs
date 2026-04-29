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
    DateTime GameDateTimeUtc,
    string Country,
    string LeagueName,
    string Season,
    string HomeTeam,
    string AwayTeam,
    short? HomeScore,
    short? AwayScore,
    string Status);

public record GameFilterOptions(
    IReadOnlyCollection<string> Countries,
    IReadOnlyCollection<string> Leagues,
    IReadOnlyCollection<string> Seasons,
    IReadOnlyCollection<string> Statuses);

public record GameBrowseSummary(
    int TotalGames,
    int FilteredGames,
    int FinishedGames,
    int ScheduledGames,
    DateTime? FirstGameUtc,
    DateTime? LastGameUtc);
