using Microsoft.Extensions.Options;

namespace Kinsmen.Web.ApiClient;

/// <summary>Every API call leaves from this server's address, so on its own the API would count all
/// visitors as one caller for rate limiting. When the shared Proxy:Key is configured, this tells the API
/// which visitor a request is for (the key proves the header came from us, not from the visitor).
/// Visitors are already rate limited here, per person, before their requests get this far.</summary>
public sealed class ProxyHeadersHandler(IHttpContextAccessor accessor, IOptions<ApiClientOptions> options) : DelegatingHandler
{
    public const string ProxyKeyHeader = "X-Kinsmen-Proxy-Key";
    public const string ClientHeader = "X-Kinsmen-Client";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = options.Value.ProxyKey;
        var context = accessor.HttpContext;
        if (!string.IsNullOrEmpty(key) && context is not null)
        {
            request.Headers.Remove(ProxyKeyHeader);
            request.Headers.Remove(ClientHeader);
            request.Headers.Add(ProxyKeyHeader, key);
            request.Headers.Add(ClientHeader, RateLimitPartitions.ClientKey(context));
        }
        return base.SendAsync(request, cancellationToken);
    }
}
