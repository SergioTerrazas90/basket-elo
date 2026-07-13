using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public interface IModelLabRunService
{
    Task<ModelLabRunCreateResponse?> CreateAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CreateModelLabRunRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ModelLabRunSummaryResponse>> ListAsync(
        Guid ownerUserId,
        int take,
        CancellationToken cancellationToken);

    Task<ModelLabRunDetailResponse?> GetAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken);

    Task<ModelLabRunPredictionPageResponse?> GetPredictionsAsync(
        Guid ownerUserId,
        Guid runId,
        int skip,
        int take,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken);
}
