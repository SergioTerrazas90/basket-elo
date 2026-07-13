namespace BasketElo.Infrastructure.Elo;

public interface IModelLabEntitlementService
{
    Task<ModelLabEntitlement> GetAsync(Guid ownerUserId, CancellationToken cancellationToken);
}
