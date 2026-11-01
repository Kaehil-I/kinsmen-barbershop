using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.ApiClient;

/// <summary>Calls /api/admin/dashboard. Separate from IKinsmenApiClient so the dashboard stays self-contained;
/// it shares that client's JSON settings and error handling, and is registered with the same bearer-token,
/// proxy and cold-start handlers (Program.cs).</summary>
public interface IDashboardApiClient
{
    Task<DashboardReport> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

public sealed class DashboardApiClient(HttpClient httpClient) : IDashboardApiClient
{
    public async Task<DashboardReport> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"/api/admin/dashboard?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<DashboardReport>(response, cancellationToken);
    }
}
