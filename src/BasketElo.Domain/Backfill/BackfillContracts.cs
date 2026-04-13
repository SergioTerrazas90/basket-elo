namespace BasketElo.Domain.Backfill;

public record BasketballProviderLeague(
    string Source,
    string SourceLeagueId,
    string Name,
    string? CountryCode);

public record BasketballProviderGame(
    string Source,
    string SourceGameId,
    DateTime GameDateTimeUtc,
    string Status,
    string SourceHomeTeamId,
    string HomeTeamName,
    string SourceAwayTeamId,
    string AwayTeamName,
    short? HomeScore,
    short? AwayScore);

public record BackfillExecutionContext(int MaxRequests, int RequestsUsed)
{
    public bool CanUseRequest() => RequestsUsed < MaxRequests;
}

public interface IBasketballDataProvider
{
    string SourceKey { get; }

    Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken);

    Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken);
}
