using BasketElo.Infrastructure.Identity;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Elo;

public sealed class ModelLabEntitlementService(
    BasketEloDbContext dbContext,
    IOptions<ModelLabPlanOptions> options) : IModelLabEntitlementService
{
    public ModelLabEntitlement GetAnonymous()
        => new("anonymous", false, false, 0, options.Value.FreeLeagueName);

    public async Task<ModelLabEntitlement> GetAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var user = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == ownerUserId)
            .Select(x => new { x.Email })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return GetAnonymous();
        }

        var normalizedEmail = AuthOptions.NormalizeEmail(user.Email);
        var isPaid = options.Value.GetNormalizedPaidEmails().Contains(normalizedEmail);
        if (isPaid)
        {
            return new ModelLabEntitlement("paid", true, true, null, null);
        }

        return new ModelLabEntitlement(
            "free",
            true,
            false,
            Math.Max(0, options.Value.FreeSavedModelLimit),
            options.Value.FreeLeagueName);
    }
}
