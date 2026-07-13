using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public interface IModelLabModelService
{
    Task<IReadOnlyCollection<ModelLabModelSummaryResponse>> ListAsync(
        Guid ownerUserId,
        bool includeArchived,
        CancellationToken cancellationToken);

    Task<ModelLabModelDetailResponse?> GetAsync(
        Guid ownerUserId,
        Guid modelId,
        CancellationToken cancellationToken);

    Task<ModelLabModelDetailResponse> CreateAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        SaveModelLabModelRequest request,
        CancellationToken cancellationToken);

    Task<ModelLabModelDetailResponse?> UpdateAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        Guid modelId,
        SaveModelLabModelRequest request,
        CancellationToken cancellationToken);

    Task<ModelLabModelDetailResponse?> SetArchivedAsync(
        Guid ownerUserId,
        Guid modelId,
        bool isArchived,
        CancellationToken cancellationToken);
}
