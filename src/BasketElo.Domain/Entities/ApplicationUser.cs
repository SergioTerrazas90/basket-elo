namespace BasketElo.Domain.Entities;

public class ApplicationUser
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PreferredCulture { get; set; }
    public string? StripeCustomerId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastLoginAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ApplicationUserExternalLogin> ExternalLogins { get; set; } = new List<ApplicationUserExternalLogin>();
    public ICollection<ApplicationUserRole> UserRoles { get; set; } = new List<ApplicationUserRole>();
    public ICollection<BillingSubscription> BillingSubscriptions { get; set; } = new List<BillingSubscription>();
    public ICollection<ModelLabModel> ModelLabModels { get; set; } = new List<ModelLabModel>();
    public ICollection<ModelLabRun> ModelLabRuns { get; set; } = new List<ModelLabRun>();
}
