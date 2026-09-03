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

    Task<string> CreatePortalSessionAsync(
        Guid userId,
        string profileUrl,
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
    bool CanManageBilling,
    string? SubscriptionStatus,
    bool CancelAtPeriodEnd);

public static class StripeBillingCadences
{
    public const string Monthly = "monthly";
    public const string Annual = "annual";
}
