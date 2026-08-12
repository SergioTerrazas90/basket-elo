namespace BasketElo.Domain.Entities;

public class Competition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? EloPoolKey { get; set; }
    public string? CountryCode { get; set; }
    public int Tier { get; set; }
    public bool IsActive { get; set; } = true;
    public string SupportPolicy { get; set; } = CompetitionSupportPolicies.Supported;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<CompetitionAlias> Aliases { get; set; } = new List<CompetitionAlias>();
}

public static class CompetitionSupportPolicies
{
    public const string Supported = "supported";
    public const string Unsupported = "unsupported";
    public const string ReviewRequired = "review_required";

    public static readonly IReadOnlyCollection<string> All =
        [Supported, Unsupported, ReviewRequired];

    public static bool IsValid(string? value) =>
        value is not null && All.Contains(value.Trim().ToLowerInvariant(), StringComparer.Ordinal);
}
