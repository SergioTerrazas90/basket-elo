using BasketElo.Domain.Entities;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BasketElo.Infrastructure.Identity;

public sealed class ApplicationUserLoginService(
    BasketEloDbContext dbContext,
    IOptions<AuthOptions> options) : IApplicationUserLoginService
{
    public async Task<ApplicationLoginResult> UpsertExternalLoginAsync(
        string provider,
        string providerUserId,
        string email,
        string displayName,
        string? avatarUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerUserId))
        {
            throw new InvalidOperationException("External login did not include a provider user id.");
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException("External login did not include an email address.");
        }

        var normalizedProvider = provider.Trim().ToLowerInvariant();
        var normalizedEmail = AuthOptions.NormalizeEmail(email);
        var now = DateTime.UtcNow;

        var login = await dbContext.ApplicationUserExternalLogins
            .Include(x => x.User)
            .ThenInclude(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(
                x => x.Provider == normalizedProvider && x.ProviderUserId == providerUserId,
                cancellationToken);

        ApplicationUser user;
        if (login is null)
        {
            user = await dbContext.ApplicationUsers
                .Include(x => x.UserRoles)
                .ThenInclude(x => x.Role)
                .SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken)
                ?? new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = now
                };

            if (dbContext.Entry(user).State == EntityState.Detached)
            {
                dbContext.ApplicationUsers.Add(user);
            }

            login = new ApplicationUserExternalLogin
            {
                Id = Guid.NewGuid(),
                User = user,
                Provider = normalizedProvider,
                ProviderUserId = providerUserId,
                CreatedAtUtc = now
            };
            dbContext.ApplicationUserExternalLogins.Add(login);
        }
        else
        {
            user = login.User;
        }

        user.Email = email.Trim();
        user.NormalizedEmail = normalizedEmail;
        user.DisplayName = string.IsNullOrWhiteSpace(displayName) ? user.Email : displayName.Trim();
        user.AvatarUrl = string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim();
        user.LastLoginAtUtc = now;

        login.EmailAtLogin = user.Email;
        login.LastLoginAtUtc = now;

        await EnsureAdminRoleAsync(user, normalizedEmail, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var roles = await dbContext.ApplicationUserRoles
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .Select(x => x.Role.Key)
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return new ApplicationLoginResult(user.Id, user.DisplayName, user.Email, user.AvatarUrl, roles);
    }

    private async Task EnsureAdminRoleAsync(
        ApplicationUser user,
        string normalizedEmail,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!options.Value.GetNormalizedAdminEmails().Contains(normalizedEmail))
        {
            return;
        }

        var adminRole = await dbContext.ApplicationRoles
            .SingleAsync(x => x.Key == ApplicationRoleKeys.Admin, cancellationToken);

        if (user.UserRoles.Any(x => x.RoleId == adminRole.Id))
        {
            return;
        }

        user.UserRoles.Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = adminRole.Id,
            CreatedAtUtc = now
        });
    }
}
