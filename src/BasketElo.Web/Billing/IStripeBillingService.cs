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
    bool WebhookEnabled,
    PremiumPlanPricing Pricing);

public sealed record PremiumPlanPricing(
    decimal MonthlyAmount,
    decimal AnnualAmount,
    string Currency)
{
    public static PremiumPlanPricing Default { get; } = new(10m, 100m, "EUR");

    public decimal AnnualMonthlyEquivalent => AnnualAmount / 12m;
    public decimal AnnualSavings => Math.Max(0m, MonthlyAmount * 12m - AnnualAmount);

    public string Format(decimal amount)
    {
        var value = amount.ToString(amount == decimal.Truncate(amount) ? "0" : "0.00");
        if (!string.Equals(Currency, "EUR", StringComparison.OrdinalIgnoreCase))
        {
            return $"{value} {Currency.ToUpperInvariant()}";
        }

        return System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es"
            ? $"{value} €"
            : $"€{value}";
    }
}

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
