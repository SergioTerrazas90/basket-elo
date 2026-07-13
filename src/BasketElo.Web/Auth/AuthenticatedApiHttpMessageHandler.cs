using System.Security.Claims;

namespace BasketElo.Web.Auth;

public sealed class AuthenticatedApiHttpMessageHandler(
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sharedSecret = configuration["InternalAuth:SharedSecret"];
        if (!string.IsNullOrWhiteSpace(sharedSecret))
        {
            request.Headers.TryAddWithoutValidation(InternalAuthHeaders.SharedSecret, sharedSecret);
        }

        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                request.Headers.TryAddWithoutValidation(InternalAuthHeaders.UserId, userId);
            }

            var email = user.FindFirstValue(ClaimTypes.Email);
            if (!string.IsNullOrWhiteSpace(email))
            {
                request.Headers.TryAddWithoutValidation(InternalAuthHeaders.Email, email);
            }

            var authMode = user.FindFirstValue(AuthClaimTypes.AuthMode) ?? user.Identity.AuthenticationType;
            if (!string.IsNullOrWhiteSpace(authMode))
            {
                request.Headers.TryAddWithoutValidation(InternalAuthHeaders.AuthMode, authMode);
            }

            var roles = user.FindAll(ClaimTypes.Role)
                .Select(x => x.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (roles.Count > 0)
            {
                request.Headers.TryAddWithoutValidation(InternalAuthHeaders.Roles, string.Join(",", roles));
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
