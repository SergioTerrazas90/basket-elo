using BasketElo.Domain.CurrentResults;

namespace BasketElo.Infrastructure.CurrentResults;

public interface ICurrentResultsIngestionService
{
    Task<CurrentResultsRunSummary> RunAsync(
        DateOnly fromDate,
        DateOnly toDate,
        bool dryRun,
        CancellationToken cancellationToken);

    Task<CurrentResultReviewResolutionDto> ResolveReviewAsync(
        Guid reviewId,
        CurrentResultReviewResolutionRequest request,
        CancellationToken cancellationToken);
}
