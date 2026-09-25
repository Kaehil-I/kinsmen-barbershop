using Microsoft.Extensions.Options;

namespace Kinsmen.Web.Auth;

/// <summary>Development-only token provider. Returns whichever dev token
/// DevTokenOptions.ActiveRole points at. Swap this out for a session/claims-based
/// provider once Kyra's real login exists — see ITokenProvider for the seam.</summary>
public sealed class DevelopmentTokenProvider(IOptionsMonitor<DevTokenOptions> options) : ITokenProvider
{
    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var config = options.CurrentValue;

        string? token = config.ActiveRole switch
        {
            "Customer" => config.Customer,
            "Barber" => config.Barber,
            "Admin" => config.Admin,
            _ => null
        };

        return Task.FromResult(string.IsNullOrWhiteSpace(token) ? null : token);
    }
}
