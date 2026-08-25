namespace BasketElo.Domain.Games;

public sealed record UpcomingGamesResponse(
    IReadOnlyCollection<UpcomingGameListItem> Games,
    DateTime FromUtc,
    DateTime ToUtc,
    string RulesetVersion,
    int TotalCount);

public sealed record UpcomingGameListItem(
    Guid Id,
    DateTime GameDateTimeUtc,
    string Country,
    string Competition,
    string HomeTeam,
    string AwayTeam,
    string Status,
    decimal? HomeElo,
    decimal? AwayElo,
    decimal? EloDifference,
    decimal? MinimumTeamElo,
    bool HasBothRatings,
    string? SourceUrl,
    int? HomeRank = null,
    int? AwayRank = null,
    decimal? HomeWinProbability = null);
