namespace BasketElo.Web.Billing;

public sealed class StripeBillingOptions
{
    public const string SectionName = "StripeBilling";

    public bool Enabled { get; set; }
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string PremiumMonthlyPriceId { get; set; } = string.Empty;
    public string PremiumAnnualPriceId { get; set; } = string.Empty;
    public bool AutomaticTaxEnabled { get; set; }

    public bool IsCheckoutConfigured => Enabled &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        (!string.IsNullOrWhiteSpace(PremiumMonthlyPriceId) ||
         !string.IsNullOrWhiteSpace(PremiumAnnualPriceId));

    public bool IsSubscriptionManagementConfigured => Enabled && !string.IsNullOrWhiteSpace(SecretKey);

    public bool IsWebhookConfigured => Enabled &&
        !string.IsNullOrWhiteSpace(SecretKey) &&
        !string.IsNullOrWhiteSpace(WebhookSecret);
}
