namespace BasketElo.Api.Auth;

public static class InternalAuthHeaders
{
    public const string SharedSecret = "X-BasketElo-Internal-Auth";
    public const string UserId = "X-BasketElo-User-Id";
    public const string Email = "X-BasketElo-User-Email";
    public const string Roles = "X-BasketElo-Roles";
    public const string AuthMode = "X-BasketElo-Auth-Mode";
}
