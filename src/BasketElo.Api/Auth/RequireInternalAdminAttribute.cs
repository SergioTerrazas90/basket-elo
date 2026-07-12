using Microsoft.AspNetCore.Mvc;

namespace BasketElo.Api.Auth;

public sealed class RequireInternalAdminAttribute : TypeFilterAttribute
{
    public RequireInternalAdminAttribute() : base(typeof(RequireInternalUserFilter))
    {
        Arguments = [true];
    }
}
