namespace BasketElo.Domain.Entities;

public class BillingSubscription
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string StripeSubscriptionId { get; set; } = string.Empty;
    public string StripeCustomerId { get; set; } = string.Empty;
    public string? StripeProductId { get; set; }
    public string? StripePriceId { get; set; }
    public bool IsPremium { get; set; }
    public string Status { get; set; } = BillingSubscriptionStatuses.Incomplete;
    public bool CancelAtPeriodEnd { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ApplicationUser User { get; set; } = null!;
}

public static class BillingSubscriptionStatuses
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string PastDue = "past_due";
    public const string Incomplete = "incomplete";
    public const string IncompleteExpired = "incomplete_expired";
    public const string Unpaid = "unpaid";
    public const string Canceled = "canceled";
    public const string Paused = "paused";
}
