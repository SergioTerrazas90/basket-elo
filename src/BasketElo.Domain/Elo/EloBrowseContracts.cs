namespace BasketElo.Domain.Elo;

public sealed record EloBrowseResponse(
    string EloPoolKey,
    string EloPoolName,
    IReadOnlyCollection<EloBrowseCountry> Countries,
    IReadOnlyCollection<EloBrowseCompetition> Competitions,
    EloBrowseContext? Context);

public sealed record EloBrowseCountry(
    string Name,
    string? CountryCode,
    int CompetitionCount,
    int TeamCount,
    int GameCount,
    DateTime? LatestGameUtc);

public sealed record EloBrowseCompetition(
    string Name,
    string Country,
    string? CountryCode,
    string Type,
    int Tier,
    int TeamCount,
    int GameCount,
    DateTime? LatestGameUtc,
    IReadOnlyCollection<string> Seasons,
    string SupportPolicy)
{
    public IReadOnlyCollection<EloBrowseTeam> Teams { get; init; } = [];
}

public sealed record EloBrowseContext(
    string? Country,
    string? Competition,
    string? CountryCode,
    string? Type,
    int? Tier,
    string SupportPolicy,
    string CoverageMessage,
    int TeamCount,
    int GameCount,
    int FinishedGameCount,
    DateTime? FirstGameUtc,
    DateTime? LatestGameUtc,
    IReadOnlyCollection<EloBrowseSeason> Seasons,
    IReadOnlyCollection<EloBrowseTierSummary> Tiers,
    IReadOnlyCollection<EloBrowseTeam> TopTeams,
    IReadOnlyCollection<EloBrowseGame> RecentGames);

public sealed record EloBrowseSeason(
    string Label,
    int GameCount,
    DateTime? LatestGameUtc);

public sealed record EloBrowseTierSummary(
    int Tier,
    int CompetitionCount,
    int TeamCount,
    int GameCount,
    decimal? AverageElo);

public sealed record EloBrowseTeam(
    Guid TeamId,
    string Name,
    string Country,
    decimal? Elo,
    int GamesPlayed,
    int Rank);

public sealed record EloBrowseGame(
    Guid Id,
    DateTime GameDateTimeUtc,
    string Season,
    string? Phase,
    string HomeTeam,
    string AwayTeam,
    short? HomeScore,
    short? AwayScore,
    string Status);

