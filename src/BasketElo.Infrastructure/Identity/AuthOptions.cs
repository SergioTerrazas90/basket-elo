namespace BasketElo.Infrastructure.Identity;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public bool Enabled { get; set; }

    public string AdminEmails { get; set; } = string.Empty;

    public IReadOnlySet<string> GetNormalizedAdminEmails()
        => AdminEmails
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeEmail)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

    public static string NormalizeEmail(string? email)
        => (email ?? string.Empty).Trim().ToUpperInvariant();
}
