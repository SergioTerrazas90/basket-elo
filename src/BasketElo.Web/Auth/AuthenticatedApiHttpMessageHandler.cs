using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace BasketElo.Web.Auth;

public sealed class AuthenticatedApiHttpMessageHandler(
    AuthenticationStateProvider authenticationStateProvider,
    IConfiguration configuration) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sharedSecret = configuration["InternalAuth:SharedSecret"];
        if (!string.IsNullOrWhiteSpace(sharedSecret))
        {
            request.Headers.TryAddWithoutValidation(InternalAuthHeaders.SharedSecret, sharedSecret);
        }

        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
            {
                request.Headers.TryAddWithoutValidation(InternalAuthHeaders.UserId, userId);
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

        return await base.SendAsync(request, cancellationToken);
    }
}
