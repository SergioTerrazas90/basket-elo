using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public interface IModelLabBacktestService
{
    Task<ModelLabOptionsResponse> GetOptionsAsync(CancellationToken cancellationToken);

    Task<ModelLabBacktestResponse> RunAsync(ModelLabBacktestRequest request, CancellationToken cancellationToken);

    Task<ModelLabBacktestExecutionResult> RunDetailedAsync(
        ModelLabBacktestRequest request,
        CancellationToken cancellationToken);
}

public sealed record ModelLabBacktestExecutionResult(
    ModelLabBacktestResponse Response,
    IReadOnlyCollection<ModelLabCompetitionOption> ScopeCompetitions,
    IReadOnlyCollection<ModelLabPredictionRow> Predictions,
    IReadOnlyCollection<ModelLabRatingRow> Ratings);
