using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;
using StripeSubscription = Stripe.Subscription;

namespace BasketElo.Web.Billing;

public sealed class StripeBillingService(
    BasketEloDbContext dbContext,
    IOptions<StripeBillingOptions> options,
    IStripeSubscriptionGateway subscriptionGateway,
    ILogger<StripeBillingService> logger) : IStripeBillingService
{
    private const string UserIdMetadataKey = "basketelo_user_id";

    public StripeBillingAvailability GetAvailability()
    {
        var value = options.Value;
        return new StripeBillingAvailability(
            value.IsCheckoutConfigured,
            value.IsCheckoutConfigured && !string.IsNullOrWhiteSpace(value.PremiumMonthlyPriceId),
            value.IsCheckoutConfigured && !string.IsNullOrWhiteSpace(value.PremiumAnnualPriceId),
            value.IsWebhookConfigured,
            value.GetPlanPricing());
    }

    public async Task<StripeBillingAccountState> GetAccountStateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var subscription = await GetCurrentPremiumSubscriptionAsync(userId, cancellationToken);
        if (subscription is not null &&
            !subscription.CurrentPeriodEndUtc.HasValue &&
            options.Value.IsSubscriptionManagementConfigured)
        {
            var subscriptionId = subscription.StripeSubscriptionId;
            try
            {
                var stripeSubscription = await subscriptionGateway.GetAsync(subscriptionId, cancellationToken);
                await SyncSubscriptionAsync(stripeSubscription, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
                subscription = await GetCurrentPremiumSubscriptionAsync(userId, cancellationToken);
            }
            catch (StripeException exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not refresh Stripe subscription {SubscriptionId} while loading billing state.",
                    subscriptionId);
            }
        }

        return ToAccountState(subscription);
    }

    public async Task<string> CreateCheckoutSessionAsync(
        Guid userId,
        string cadence,
        string profileUrl,
        CancellationToken cancellationToken)
    {
        var value = options.Value;
        if (!value.IsCheckoutConfigured)
        {
            throw new InvalidOperationException("Stripe Checkout is not configured.");
        }

        var priceId = cadence switch
        {
            StripeBillingCadences.Monthly when !string.IsNullOrWhiteSpace(value.PremiumMonthlyPriceId)
                => value.PremiumMonthlyPriceId,
            StripeBillingCadences.Annual when !string.IsNullOrWhiteSpace(value.PremiumAnnualPriceId)
                => value.PremiumAnnualPriceId,
            _ => throw new InvalidOperationException("The selected Premium billing cadence is not available.")
        };

        var user = await dbContext.ApplicationUsers
            .SingleOrDefaultAsync(x => x.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException("The signed-in BasketElo account could not be found.");

        var alreadySubscribed = await dbContext.BillingSubscriptions.AnyAsync(
            subscription => subscription.UserId == userId &&
                subscription.IsPremium &&
                (subscription.Status == BillingSubscriptionStatuses.Active ||
                 subscription.Status == BillingSubscriptionStatuses.Trialing ||
                 subscription.Status == BillingSubscriptionStatuses.PastDue),
            cancellationToken);
        if (alreadySubscribed)
        {
            throw new InvalidOperationException("This account already has Premium. Manage the subscription from the billing portal.");
        }

        var client = CreateClient(value.SecretKey);
        if (string.IsNullOrWhiteSpace(user.StripeCustomerId))
        {
            var customer = await new CustomerService(client).CreateAsync(
                new CustomerCreateOptions
                {
                    Email = user.Email,
                    Name = user.DisplayName,
                    Metadata = new Dictionary<string, string>
                    {
                        [UserIdMetadataKey] = user.Id.ToString("D")
                    }
                },
                cancellationToken: cancellationToken);
            user.StripeCustomerId = customer.Id;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var session = await new SessionService(client).CreateAsync(
            new SessionCreateOptions
            {
                Customer = user.StripeCustomerId,
                ClientReferenceId = user.Id.ToString("D"),
                Mode = "subscription",
                SuccessUrl = AddQuery(profileUrl, "billing=success&session_id={CHECKOUT_SESSION_ID}"),
                CancelUrl = AddQuery(profileUrl, "billing=canceled"),
                AllowPromotionCodes = true,
                BillingAddressCollection = "auto",
                AutomaticTax = new SessionAutomaticTaxOptions
                {
                    Enabled = value.AutomaticTaxEnabled
                },
                TaxIdCollection = new SessionTaxIdCollectionOptions
                {
                    Enabled = true
                },
                CustomerUpdate = new SessionCustomerUpdateOptions
                {
                    Address = "auto",
                    Name = "auto"
                },
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                ],
                Metadata = new Dictionary<string, string>
                {
                    [UserIdMetadataKey] = user.Id.ToString("D")
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        [UserIdMetadataKey] = user.Id.ToString("D")
                    }
                }
            },
            cancellationToken: cancellationToken);

        return session.Url ?? throw new InvalidOperationException("Stripe did not return a Checkout URL.");
    }

    public async Task<StripeBillingAccountState> SetCancelAtPeriodEndAsync(
        Guid userId,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken)
    {
        var value = options.Value;
        if (!value.IsSubscriptionManagementConfigured)
        {
            throw new InvalidOperationException("Subscription management is not configured.");
        }

        var subscription = await GetCurrentPremiumSubscriptionAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("This account does not have an active Premium subscription to update.");

        var updated = await subscriptionGateway.SetCancelAtPeriodEndAsync(
            subscription.StripeSubscriptionId,
            cancelAtPeriodEnd,
            cancellationToken);

        await SyncSubscriptionAsync(updated, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToAccountState(await GetCurrentPremiumSubscriptionAsync(userId, cancellationToken));
    }

    public async Task ProcessWebhookAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken)
    {
        var value = options.Value;
        if (!value.IsWebhookConfigured)
        {
            throw new InvalidOperationException("Stripe webhooks are not configured.");
        }

        var stripeEvent = EventUtility.ConstructEvent(payload, signatureHeader, value.WebhookSecret);
        if (await dbContext.StripeWebhookEvents.AnyAsync(x => x.Id == stripeEvent.Id, cancellationToken))
        {
            return;
        }

        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted when stripeEvent.Data.Object is Session checkoutSession:
                await SyncCheckoutSessionAsync(checkoutSession, value.SecretKey, cancellationToken);
                break;
            case EventTypes.CustomerSubscriptionCreated:
            case EventTypes.CustomerSubscriptionUpdated:
            case EventTypes.CustomerSubscriptionDeleted:
                if (stripeEvent.Data.Object is StripeSubscription subscription)
                {
                    await SyncSubscriptionAsync(subscription, cancellationToken);
                }
                break;
        }

        dbContext.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            Id = stripeEvent.Id,
            EventType = stripeEvent.Type,
            ProcessedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncCheckoutSessionAsync(
        Session session,
        string secretKey,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(session.ClientReferenceId, session.Metadata, out var userId))
        {
            logger.LogWarning("Stripe Checkout Session {SessionId} has no valid BasketElo user reference.", session.Id);
            return;
        }

        var user = await dbContext.ApplicationUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Stripe Checkout Session {SessionId} references unknown BasketElo user {UserId}.", session.Id, userId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.CustomerId))
        {
            user.StripeCustomerId = session.CustomerId;
        }

        if (!string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            var subscription = await new SubscriptionService(CreateClient(secretKey))
                .GetAsync(session.SubscriptionId, cancellationToken: cancellationToken);
            await SyncSubscriptionAsync(subscription, cancellationToken);
        }
    }

    private async Task SyncSubscriptionAsync(
        StripeSubscription subscription,
        CancellationToken cancellationToken)
    {
        var stored = await dbContext.BillingSubscriptions
            .SingleOrDefaultAsync(x => x.StripeSubscriptionId == subscription.Id, cancellationToken);
        var userId = stored?.UserId;

        if (!userId.HasValue && TryResolveUserId(null, subscription.Metadata, out var metadataUserId))
        {
            userId = metadataUserId;
        }

        if (!userId.HasValue && !string.IsNullOrWhiteSpace(subscription.CustomerId))
        {
            userId = await dbContext.ApplicationUsers
                .Where(x => x.StripeCustomerId == subscription.CustomerId)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (!userId.HasValue)
        {
            logger.LogWarning("Stripe Subscription {SubscriptionId} could not be linked to a BasketElo user.", subscription.Id);
            return;
        }

        var user = await dbContext.ApplicationUsers.SingleOrDefaultAsync(x => x.Id == userId.Value, cancellationToken);
        if (user is null)
        {
            logger.LogWarning("Stripe Subscription {SubscriptionId} references unknown BasketElo user {UserId}.", subscription.Id, userId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(subscription.CustomerId))
        {
            user.StripeCustomerId = subscription.CustomerId;
        }

        stored ??= new BillingSubscription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            StripeSubscriptionId = subscription.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        var price = subscription.Items?.Data?.FirstOrDefault()?.Price;
        stored.StripeCustomerId = subscription.CustomerId ?? user.StripeCustomerId ?? string.Empty;
        stored.StripePriceId = price?.Id;
        stored.StripeProductId = price?.ProductId;
        stored.IsPremium = IsPremiumPrice(price?.Id);
        stored.Status = subscription.Status;
        stored.CancelAtPeriodEnd = subscription.CancelAtPeriodEnd;
        stored.PremiumStartedAtUtc = EnsureUtc(subscription.StartDate);
        if (subscription.Items?.Data?.FirstOrDefault() is { } item)
        {
            stored.CurrentPeriodStartUtc = EnsureUtc(item.CurrentPeriodStart);
            stored.CurrentPeriodEndUtc = EnsureUtc(item.CurrentPeriodEnd);
        }
        stored.UpdatedAtUtc = DateTime.UtcNow;

        if (dbContext.Entry(stored).State == EntityState.Detached)
        {
            dbContext.BillingSubscriptions.Add(stored);
        }
    }

    private static bool TryResolveUserId(
        string? reference,
        IReadOnlyDictionary<string, string>? metadata,
        out Guid userId)
    {
        if (Guid.TryParse(reference, out userId))
        {
            return true;
        }

        return metadata is not null &&
            metadata.TryGetValue(UserIdMetadataKey, out var metadataUserId) &&
            Guid.TryParse(metadataUserId, out userId);
    }

    private static StripeClient CreateClient(string secretKey) => new(secretKey.Trim());

    private Task<BillingSubscription?> GetCurrentPremiumSubscriptionAsync(
        Guid userId,
        CancellationToken cancellationToken)
        => dbContext.BillingSubscriptions
            .Where(subscription =>
                subscription.UserId == userId &&
                subscription.IsPremium &&
                (subscription.Status == BillingSubscriptionStatuses.Active ||
                 subscription.Status == BillingSubscriptionStatuses.Trialing ||
                 subscription.Status == BillingSubscriptionStatuses.PastDue))
            .OrderByDescending(subscription => subscription.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private StripeBillingAccountState ToAccountState(BillingSubscription? subscription)
        => new(
            options.Value.IsSubscriptionManagementConfigured && subscription is not null,
            subscription?.Status,
            subscription?.CancelAtPeriodEnd == true,
            subscription?.CurrentPeriodEndUtc);

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private bool IsPremiumPrice(string? priceId)
    {
        var value = options.Value;
        return !string.IsNullOrWhiteSpace(priceId) &&
            (string.Equals(priceId, value.PremiumMonthlyPriceId, StringComparison.Ordinal) ||
             string.Equals(priceId, value.PremiumAnnualPriceId, StringComparison.Ordinal));
    }

    private static string AddQuery(string url, string query)
        => $"{url}{(url.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{query}";
}
