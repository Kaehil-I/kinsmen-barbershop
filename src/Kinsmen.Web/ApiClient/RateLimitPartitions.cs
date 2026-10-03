using System.Security.Claims;

namespace Kinsmen.Web.ApiClient;

/// <summary>One rate-limit bucket per person: the signed-in account when there is one, otherwise the
/// visitor's IP address (Render forwards the real one; see ASPNETCORE_FORWARDEDHEADERS_ENABLED).</summary>
public static class RateLimitPartitions
{
    public static string ClientKey(HttpContext context)
    {
        var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? context.User.FindFirst("sub")?.Value;
        return !string.IsNullOrEmpty(subject)
            ? "user:" + subject
            : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
}
