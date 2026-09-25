using System.Net.Http.Headers;
using Kinsmen.Web.Auth;

namespace Kinsmen.Web.ApiClient;

/// <summary>Attaches "Authorization: Bearer {token}" to every request the typed client
/// sends, using whatever ITokenProvider is registered. Public endpoints ignore the header
/// harmlessly, so this can run unconditionally rather than needing each call site to know
/// which endpoints are protected.</summary>
public sealed class BearerTokenHandler(ITokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);

        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
