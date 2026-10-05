using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Infrastructure;

// Credentials for an Auth0 Machine-to-Machine application allowed only read:users and
// update:users_app_metadata on the Auth0 Management API. Staff management is off when any are missing.
public sealed class Auth0ManagementOptions
{
    public string? Domain { get; init; }
    public string? ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Domain) && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}

// Management API tokens last about a day; one is fetched and reused until shortly before it expires.
public sealed class Auth0TokenCache
{
    private readonly SemaphoreSlim gate = new(1);
    private string? token;
    private DateTimeOffset expires;

    public async Task<string> Get(Func<CancellationToken, Task<(string Token, int ExpiresInSeconds)>> fetch,
        TimeProvider clock, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (token is null || clock.GetUtcNow() >= expires)
            {
                var (fresh, seconds) = await fetch(ct);
                token = fresh;
                expires = clock.GetUtcNow().AddSeconds(Math.Max(0, seconds - 60));
            }
            return token;
        }
        finally { gate.Release(); }
    }

    public void Clear() => token = null;
}

public sealed class Auth0ManagementDirectory(HttpClient http, Auth0ManagementOptions options, Auth0TokenCache cache,
    TimeProvider clock, ILogger<Auth0ManagementDirectory> logger) : IIdentityDirectory
{
    private const string Fields = "fields=user_id,email,email_verified,name,app_metadata&include_fields=true";
    public bool IsConfigured => options.IsConfigured;

    public async Task<List<IdentityUser>> FindByEmail(string email, CancellationToken ct)
    {
        using var response = await Send(HttpMethod.Get, $"api/v2/users-by-email?email={Uri.EscapeDataString(email)}&{Fields}", null, ct);
        return (await Read(response, ct)).EnumerateArray().Select(ToUser).ToList();
    }

    public async Task<IdentityUser?> FindById(string userId, CancellationToken ct)
    {
        using var response = await Send(HttpMethod.Get, $"api/v2/users/{Uri.EscapeDataString(userId)}?{Fields}", null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return ToUser(await Read(response, ct));
    }

    public async Task<List<IdentityUser>> ListStaff(CancellationToken ct)
    {
        var query = Uri.EscapeDataString("app_metadata.role:\"Admin\" OR app_metadata.role:\"Barber\"");
        using var response = await Send(HttpMethod.Get, $"api/v2/users?q={query}&search_engine=v3&per_page=100&{Fields}", null, ct);
        return (await Read(response, ct)).EnumerateArray().Select(ToUser).ToList();
    }

    public async Task SetRole(string userId, string role, CancellationToken ct)
    {
        // Auth0 merges app_metadata on PATCH, so only the role key changes.
        using var response = await Send(HttpMethod.Patch, $"api/v2/users/{Uri.EscapeDataString(userId)}",
            new { app_metadata = new { role } }, ct);
        await Read(response, ct);
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await cache.Get(FetchToken, clock, ct));
            if (body is not null) request.Content = JsonContent.Create(body);
            var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized) cache.Clear();
            return response;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Auth0 Management API unreachable: {ErrorType}", e.GetType().Name);
            throw new DomainError(503, "identity_provider_unavailable", "Couldn't reach Auth0. Try again shortly.");
        }
    }

    private async Task<(string, int)> FetchToken(CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("oauth/token", new
        {
            grant_type = "client_credentials",
            client_id = options.ClientId,
            client_secret = options.ClientSecret,
            audience = $"https://{options.Domain}/api/v2/"
        }, ct);
        if (!response.IsSuccessStatusCode)
        {
            // Never log or return Auth0's body: it can echo request details.
            logger.LogWarning("Auth0 Management token request failed: {Status}", (int)response.StatusCode);
            throw new DomainError(502, "identity_provider_error", "Auth0 rejected the staff-management credentials.");
        }
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return (json.GetProperty("access_token").GetString()!, json.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600);
    }

    private async Task<JsonElement> Read(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        logger.LogWarning("Auth0 Management API request failed: {Status}", (int)response.StatusCode);
        throw response.StatusCode == HttpStatusCode.TooManyRequests
            ? new DomainError(503, "identity_provider_busy", "Auth0 is rate limiting requests. Try again in a minute.")
            : new DomainError(502, "identity_provider_error", $"Auth0 rejected the request ({(int)response.StatusCode}).");
    }

    private static IdentityUser ToUser(JsonElement user)
    {
        static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var role = user.TryGetProperty("app_metadata", out var meta) && meta.ValueKind == JsonValueKind.Object ? Text(meta, "role") : null;
        return new IdentityUser(Text(user, "user_id") ?? "", Text(user, "email"),
            user.TryGetProperty("email_verified", out var v) && v.ValueKind == JsonValueKind.True,
            Text(user, "name"), StaffService.Roles.Contains(role) ? role! : "Customer");
    }
}

public sealed class DisabledIdentityDirectory : IIdentityDirectory
{
    public bool IsConfigured => false;
    public Task<List<IdentityUser>> FindByEmail(string email, CancellationToken ct) => throw new InvalidOperationException("Not configured.");
    public Task<IdentityUser?> FindById(string userId, CancellationToken ct) => throw new InvalidOperationException("Not configured.");
    public Task<List<IdentityUser>> ListStaff(CancellationToken ct) => throw new InvalidOperationException("Not configured.");
    public Task SetRole(string userId, string role, CancellationToken ct) => throw new InvalidOperationException("Not configured.");
}
