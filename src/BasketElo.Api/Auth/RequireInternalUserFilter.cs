using BasketElo.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BasketElo.Api.Auth;

public sealed class RequireInternalUserFilter(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    bool requireAdmin = false) : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expectedSecret = configuration["InternalAuth:SharedSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            if (!environment.IsDevelopment())
            {
                context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
                return;
            }
        }
        else if (!string.Equals(
            context.HttpContext.Request.Headers[InternalAuthHeaders.SharedSecret].ToString(),
            expectedSecret,
            StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userId = context.HttpContext.Request.Headers[InternalAuthHeaders.UserId].ToString();
        if (!Guid.TryParse(userId, out _))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (!requireAdmin)
        {
            return;
        }

        var roles = context.HttpContext.Request.Headers[InternalAuthHeaders.Roles]
            .ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!roles.Contains(ApplicationRoleKeys.Admin, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new ForbidResult();
        }
    }
}
