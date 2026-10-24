using System.Net.Http.Json;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.ApiClient;

/// <summary>Calls /api/me/profile. Separate from IKinsmenApiClient so the profile feature stays
/// self-contained; it shares that client's JSON settings and error handling, and is registered with
/// the same bearer-token and proxy handlers (Program.cs).</summary>
public interface IProfileApiClient
{
    Task<CustomerProfile> GetAsync(CancellationToken cancellationToken = default);
    Task<CustomerProfile> SaveAsync(ProfileInput input, CancellationToken cancellationToken = default);
}

public sealed class ProfileApiClient(HttpClient httpClient) : IProfileApiClient
{
    public async Task<CustomerProfile> GetAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/me/profile", cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<CustomerProfile>(response, cancellationToken);
    }

    public async Task<CustomerProfile> SaveAsync(ProfileInput input, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync("/api/me/profile", input, KinsmenApiClient.JsonOptions, cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<CustomerProfile>(response, cancellationToken);
    }
}
