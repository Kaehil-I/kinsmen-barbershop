using Microsoft.AspNetCore.Authentication;

namespace Kinsmen.Web.Auth;

/// <summary>Sends the signed-in user's Auth0 access token to the API. Anonymous visitors
/// get null, so public endpoints (services, barbers, availability) keep working. This is
/// the ITokenProvider seam DevelopmentTokenProvider used to fill — see ITokenProvider.cs.</summary>
public sealed class Auth0TokenProvider(IHttpContextAccessor accessor) : ITokenProvider
{
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var context = accessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true) return null;
        return await context.GetTokenAsync("access_token");
    }
}