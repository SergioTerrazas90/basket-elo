using BasketElo.Domain.Entities;
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
        => new("anonymous", false, false, 0, 0, null, options.Value.FreeLeagueName);

    public async Task<ModelLabEntitlement> GetAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var user = await dbContext.ApplicationUsers
            .AsNoTracking()
            .Where(x => x.Id == ownerUserId)
            .Select(x => new
            {
                x.Email,
                x.CreatedAtUtc,
                IsAdmin = x.UserRoles.Any(userRole => userRole.Role.Key == ApplicationRoleKeys.Admin),
                HasStripePremium = x.BillingSubscriptions.Any(subscription =>
                    subscription.IsPremium &&
                    (subscription.Status == BillingSubscriptionStatuses.Active ||
                     subscription.Status == BillingSubscriptionStatuses.Trialing ||
                     subscription.Status == BillingSubscriptionStatuses.PastDue)),
                PremiumStartedAtUtc = x.BillingSubscriptions
                    .Where(subscription =>
                        subscription.IsPremium &&
                        (subscription.Status == BillingSubscriptionStatuses.Active ||
                         subscription.Status == BillingSubscriptionStatuses.Trialing ||
                         subscription.Status == BillingSubscriptionStatuses.PastDue))
                    .OrderByDescending(subscription => subscription.UpdatedAtUtc)
                    .Select(subscription => subscription.PremiumStartedAtUtc)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return GetAnonymous();
        }

        var normalizedEmail = AuthOptions.NormalizeEmail(user.Email);
        var isPaid = user.IsAdmin ||
            user.HasStripePremium ||
            options.Value.GetNormalizedPaidEmails().Contains(normalizedEmail);
        if (isPaid)
        {
            var window = GetMonthlyWindow(user.PremiumStartedAtUtc ?? user.CreatedAtUtc, DateTime.UtcNow);
            return new ModelLabEntitlement(
                "paid",
                true,
                true,
                null,
                Math.Max(0, options.Value.PaidStoredRunLimit),
                Math.Max(0, options.Value.PaidMonthlyRunLimit),
                null,
                window.StartUtc,
                window.EndUtc);
        }

        return new ModelLabEntitlement(
            "free",
            true,
            false,
            Math.Max(0, options.Value.FreeSavedModelLimit),
            Math.Max(0, options.Value.FreeStoredRunLimit),
            null,
            options.Value.FreeLeagueName);
    }

    internal static (DateTime StartUtc, DateTime EndUtc) GetMonthlyWindow(DateTime anchorUtc, DateTime nowUtc)
    {
        var anchor = EnsureUtc(anchorUtc);
        var now = EnsureUtc(nowUtc);
        var elapsedMonths = ((now.Year - anchor.Year) * 12) + now.Month - anchor.Month;
        if (anchor.AddMonths(elapsedMonths) > now)
        {
            elapsedMonths--;
        }

        elapsedMonths = Math.Max(0, elapsedMonths);
        return (anchor.AddMonths(elapsedMonths), anchor.AddMonths(elapsedMonths + 1));
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
