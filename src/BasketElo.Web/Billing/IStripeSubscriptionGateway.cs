using Microsoft.Extensions.Options;
using Stripe;

namespace BasketElo.Web.Billing;

public interface IStripeSubscriptionGateway
{
    Task<Subscription> GetAsync(string subscriptionId, CancellationToken cancellationToken);

    Task<Subscription> SetCancelAtPeriodEndAsync(
        string subscriptionId,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken);
}

public sealed class StripeSubscriptionGateway(IOptions<StripeBillingOptions> options) : IStripeSubscriptionGateway
{
    public Task<Subscription> GetAsync(string subscriptionId, CancellationToken cancellationToken)
        => CreateService().GetAsync(subscriptionId, cancellationToken: cancellationToken);

    public Task<Subscription> SetCancelAtPeriodEndAsync(
        string subscriptionId,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken)
        => CreateService().UpdateAsync(
            subscriptionId,
            new SubscriptionUpdateOptions { CancelAtPeriodEnd = cancelAtPeriodEnd },
            cancellationToken: cancellationToken);

    private SubscriptionService CreateService()
        => new(new StripeClient(options.Value.SecretKey.Trim()));
}
