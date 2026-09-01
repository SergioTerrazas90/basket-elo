using BasketElo.Domain.Entities;
using Hangfire.Dashboard;

namespace BasketElo.Web.Auth;

public sealed class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole(ApplicationRoleKeys.Admin);
    }
}
