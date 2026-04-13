using BasketElo.Domain.Backfill;

namespace BasketElo.Infrastructure.Backfill;

public interface IBackfillCoverageService
{
    Task<BackfillCoverageResponse> GetCoverageAsync(
        string? provider,
        string? country,
        string? leagueName,
        CancellationToken cancellationToken);
}
