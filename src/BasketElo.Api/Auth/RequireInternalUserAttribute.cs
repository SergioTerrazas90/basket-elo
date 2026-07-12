using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Auth;

public sealed class RequireInternalUserAttribute : TypeFilterAttribute
{
    public RequireInternalUserAttribute() : base(typeof(RequireInternalUserFilter))
    {
    }
}
