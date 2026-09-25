namespace Kinsmen.Web.Auth;

/// <summary>Supplies the bearer token for outgoing API calls. The only implementation
/// today (DevelopmentTokenProvider) reads one of Zario's short-lived dev tokens from
/// configuration. When Kyra's real login lands, swap in an implementation that reads
/// the token from the authenticated HttpContext instead — nothing else in the app
/// (KinsmenApiClient, controllers) needs to change.</summary>
public interface ITokenProvider
{
    /// <summary>Returns the token to send as "Authorization: Bearer {token}", or null if
    /// no token is configured (public endpoints still work without one).</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
