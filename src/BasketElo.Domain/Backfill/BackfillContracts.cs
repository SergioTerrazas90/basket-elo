namespace BasketElo.Domain.Backfill;

public record BasketballProviderLeague(
    string Source,
    string SourceLeagueId,
    string Name,
    string? CountryCode,
    string SeasonParameterFormat = "default");

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
    short? AwayScore,
    BasketballProviderGameProvenance? Provenance = null,
    string? ExclusionReason = null,
    string? CompetitionPhase = null,
    string? CompetitionRound = null,
    string? SourceHomeTeamCountryCode = null,
    string? SourceAwayTeamCountryCode = null);

public sealed record BasketballProviderGameProvenance(
    string? SourceUrl,
    string? SourceSeasonKey,
    DateTime? FetchedAtUtc,
    string ParserVersion,
    string? SourceRevision = null);

public class BackfillExecutionContext(int maxRequests, int requestsUsed)
{
    public int MaxRequests { get; } = maxRequests;

    public int RequestsUsed { get; private set; } = requestsUsed;

    // A non-positive limit means unlimited. This is required for archive
    // tournaments whose stage and gameweek/page counts are discovered at run time.
    public bool CanUseRequest() => MaxRequests <= 0 || RequestsUsed < MaxRequests;

    public void ConsumeRequest()
    {
        if (!CanUseRequest())
        {
            throw new InvalidOperationException($"Backfill request budget reached (maxRequests={MaxRequests}).");
        }

        RequestsUsed += 1;
    }
}

public interface IBasketballDataProvider
{
    string SourceKey { get; }

    Task<BasketballProviderLeague?> ResolveLeagueAsync(
        string country,
        string leagueName,
        BackfillExecutionContext context,
        CancellationToken cancellationToken);

    Task<(IReadOnlyCollection<BasketballProviderGame> Games, bool HasMorePages, IReadOnlyCollection<string> Warnings)> GetGamesAsync(
        BasketballProviderLeague league,
        string season,
        BackfillExecutionContext context,
        CancellationToken cancellationToken);
}
