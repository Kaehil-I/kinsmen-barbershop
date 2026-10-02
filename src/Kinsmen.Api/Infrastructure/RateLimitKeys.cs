using System.Security.Cryptography;
using System.Text;

namespace Kinsmen.Api.Infrastructure;

// Chooses the rate-limit bucket for a request: the signed-in user, else the visitor Kinsmen.Web vouches for,
// else the caller's IP address.
public static class RateLimitKeys
{
    public const string ProxyKeyHeader = "X-Kinsmen-Proxy-Key";
    public const string ClientHeader = "X-Kinsmen-Client";

    public static string For(HttpContext context, string? proxyKey)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(subject)) return "user:" + subject;
        if (IsTrustedProxy(context.Request, proxyKey)
            && context.Request.Headers[ClientHeader] is { Count: 1 } client
            && client[0] is { Length: > 0 and <= 128 } visitor)
            return "client:" + visitor;
        return "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    // Only the web app knows the key, so a caller can't pick its own bucket by sending X-Kinsmen-Client.
    private static bool IsTrustedProxy(HttpRequest request, string? proxyKey)
    {
        if (string.IsNullOrEmpty(proxyKey) || request.Headers[ProxyKeyHeader] is not { Count: 1 } supplied) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied[0] ?? ""), Encoding.UTF8.GetBytes(proxyKey));
    }
}
