using System.Net.Http.Json;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.ApiClient;

/// <summary>Calls /api/admin/staff/*. Separate from IKinsmenApiClient so staff management stays
/// self-contained; it shares that client's JSON settings and error handling, and is registered with
/// the same bearer-token and proxy handlers (Program.cs).</summary>
public interface IStaffApiClient
{
    Task<StaffStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<List<StaffMember>> GetStaffAsync(CancellationToken cancellationToken = default);
    Task<List<StaffMember>> LookupAsync(string email, CancellationToken cancellationToken = default);
    Task<StaffMember> SetRoleAsync(string userId, string role, CancellationToken cancellationToken = default);
    Task<List<StaffAuditEntry>> GetAuditAsync(CancellationToken cancellationToken = default);
}

public sealed class StaffApiClient(HttpClient httpClient) : IStaffApiClient
{
    public async Task<StaffStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/admin/staff/status", cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<StaffStatus>(response, cancellationToken);
    }

    public async Task<List<StaffMember>> GetStaffAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/admin/staff", cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<List<StaffMember>>(response, cancellationToken);
    }

    public async Task<List<StaffMember>> LookupAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"/api/admin/staff/lookup?email={Uri.EscapeDataString(email)}", cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<List<StaffMember>>(response, cancellationToken);
    }

    public async Task<StaffMember> SetRoleAsync(string userId, string role, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync(
            $"/api/admin/staff/{Uri.EscapeDataString(userId)}/role", new RoleChangeInput { Role = role },
            KinsmenApiClient.JsonOptions, cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<StaffMember>(response, cancellationToken);
    }

    public async Task<List<StaffAuditEntry>> GetAuditAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/admin/staff/audit", cancellationToken);
        return await KinsmenApiClient.ReadOrThrowAsync<List<StaffAuditEntry>>(response, cancellationToken);
    }
}
