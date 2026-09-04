using System.Security.Claims;
using BasketElo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BasketElo.Web.Localization;

public sealed class UserCultureResolver(BasketEloDbContext dbContext)
{
    public async Task<UserCultureResolution> ResolveAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var hasCookieCulture = SupportedCultures.TryNormalize(
            httpContext.Request.Cookies[SupportedCultures.CultureCookieName],
            out var cookieCulture);

        var userId = GetAuthenticatedUserId(httpContext.User);
        if (userId.HasValue)
        {
            var user = await dbContext.ApplicationUsers
                .SingleOrDefaultAsync(x => x.Id == userId.Value, cancellationToken);

            if (user is not null)
            {
                if (SupportedCultures.TryNormalize(user.PreferredCulture, out var storedCulture))
                {
                    return new UserCultureResolution(
                        storedCulture,
                        PersistCookie: !hasCookieCulture || cookieCulture != storedCulture);
                }

                var initialCulture = hasCookieCulture
                    ? cookieCulture
                    : CultureInference.Infer(httpContext.Request) ?? SupportedCultures.English;

                user.PreferredCulture = initialCulture;
                await dbContext.SaveChangesAsync(cancellationToken);
                return new UserCultureResolution(initialCulture, PersistCookie: !hasCookieCulture);
            }
        }

        if (hasCookieCulture)
        {
            return new UserCultureResolution(cookieCulture, PersistCookie: false);
        }

        return new UserCultureResolution(
            CultureInference.Infer(httpContext.Request) ?? SupportedCultures.English,
            PersistCookie: true);
    }

    private static Guid? GetAuthenticatedUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var userId) ? userId : null;
    }
}

public sealed record UserCultureResolution(string CultureName, bool PersistCookie);
