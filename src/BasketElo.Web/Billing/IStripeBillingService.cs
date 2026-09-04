namespace BasketElo.Web.Billing;

public interface IStripeBillingService
{
    StripeBillingAvailability GetAvailability();

    Task<StripeBillingAccountState> GetAccountStateAsync(Guid userId, CancellationToken cancellationToken);

    Task<string> CreateCheckoutSessionAsync(
        Guid userId,
        string cadence,
        string profileUrl,
        CancellationToken cancellationToken);

    Task<StripeBillingAccountState> SetCancelAtPeriodEndAsync(
        Guid userId,
        bool cancelAtPeriodEnd,
        CancellationToken cancellationToken);

    Task ProcessWebhookAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken);
}

public sealed record StripeBillingAvailability(
    bool CheckoutEnabled,
    bool MonthlyEnabled,
    bool AnnualEnabled,
    bool WebhookEnabled);

public sealed record StripeBillingAccountState(
    bool CanChangeCancellation,
    string? SubscriptionStatus,
    bool CancelAtPeriodEnd,
    DateTime? CurrentPeriodEndUtc);

public static class StripeBillingCadences
{
    public const string Monthly = "monthly";
    public const string Annual = "annual";
}
