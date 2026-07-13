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
        SaveModelLabModelRequest request,
        CancellationToken cancellationToken);

    Task<ModelLabModelDetailResponse?> UpdateAsync(
        Guid ownerUserId,
        Guid modelId,
        SaveModelLabModelRequest request,
        CancellationToken cancellationToken);

    Task<ModelLabModelDetailResponse?> SetArchivedAsync(
        Guid ownerUserId,
        Guid modelId,
        bool isArchived,
        CancellationToken cancellationToken);
}
