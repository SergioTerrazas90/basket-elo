namespace BasketElo.Infrastructure.Identity;

public interface IApplicationUserLoginService
{
    Task<ApplicationLoginResult> UpsertExternalLoginAsync(
        string provider,
        string providerUserId,
        string email,
        string displayName,
        string? avatarUrl,
        CancellationToken cancellationToken);
}
