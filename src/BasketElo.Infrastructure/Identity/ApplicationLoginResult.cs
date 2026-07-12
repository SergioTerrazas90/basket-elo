namespace BasketElo.Infrastructure.Identity;

public sealed record ApplicationLoginResult(
    Guid UserId,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    IReadOnlyCollection<string> Roles);
