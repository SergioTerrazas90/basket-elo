using BasketElo.Domain.Elo;

namespace BasketElo.Infrastructure.Elo;

public interface IModelLabRunService
{
    Task<ModelLabRunCreateResponse?> CreateAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CreateModelLabRunRequest request,
        CancellationToken cancellationToken);

    Task<ModelLabComparisonCreateResponse> CreateComparisonAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
        CreateModelLabComparisonRequest request,
        CancellationToken cancellationToken);

    Task<ModelLabSavedComparisonResponse?> GetLatestCompatibleComparisonAsync(
        Guid ownerUserId,
        IReadOnlyCollection<Guid> modelIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ModelLabSavedComparisonResponse>> ListCompatibleComparisonsAsync(
        Guid ownerUserId,
        int take,
        CancellationToken cancellationToken);

    Task ExecuteAsync(Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ModelLabRunSummaryResponse>> ListAsync(
        Guid ownerUserId,
        int take,
        Guid? modelId,
        CancellationToken cancellationToken);

    Task<ModelLabRunQuotaResponse> GetQuotaAsync(
        Guid ownerUserId,
        ModelLabEntitlement entitlement,
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

    Task<ModelLabRunEvolutionResponse?> GetEvolutionAsync(
        Guid ownerUserId,
        Guid runId,
        int teamCount,
        int pointsPerTeam,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken);

    Task<ModelLabRunSummaryResponse?> CancelAsync(
        Guid ownerUserId,
        Guid runId,
        CancellationToken cancellationToken);

    Task<ModelLabRunSummaryResponse?> RetryAsync(
        Guid ownerUserId,
        Guid runId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken);

    Task<ModelLabRunSummaryResponse?> RetainAsync(
        Guid ownerUserId,
        Guid runId,
        ModelLabEntitlement entitlement,
        CancellationToken cancellationToken);

    Task<int> CleanupExpiredTemporaryRunsAsync(CancellationToken cancellationToken);
}
