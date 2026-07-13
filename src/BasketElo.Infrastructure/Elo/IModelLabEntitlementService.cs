namespace BasketElo.Infrastructure.Elo;

public interface IModelLabEntitlementService
{
    ModelLabEntitlement GetAnonymous();

    Task<ModelLabEntitlement> GetAsync(Guid ownerUserId, CancellationToken cancellationToken);
}
