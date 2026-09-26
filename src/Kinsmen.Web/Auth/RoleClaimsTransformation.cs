using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Kinsmen.Web.Auth;

/// <summary>Auth0's role claim comes through as a plain "role" claim (set by the
/// tenant's Post-Login Action - see docs/auth/AUTH0-SETUP.md Part 3), not the standard
/// ClaimTypes.Role ASP.NET Core's [Authorize(Roles = "...")] and User.IsInRole(...)
/// look for. This copies it across once per sign-in so both of those work normally
/// everywhere, instead of every check needing to know the literal claim name "role".</summary>
public sealed class RoleClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        var role = principal.FindFirst("role")?.Value;

        if (identity is not null && role is not null && !principal.IsInRole(role))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return Task.FromResult(principal);
    }
}
