using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.ApiClient;

/// <summary>Default IKinsmenApiClient implementation over a typed HttpClient. Register via
/// builder.Services.AddHttpClient&lt;IKinsmenApiClient, KinsmenApiClient&gt;() with
/// BaseAddress set from configuration (Api:BaseUrl) and BearerTokenHandler attached —
/// see Program.cs.</summary>
public sealed class KinsmenApiClient(HttpClient httpClient) : IKinsmenApiClient
{
    // The API uses camelCase JSON and lowercase-first-letter enum values (pending,
    // confirmed, ..., noShow). This must match on both serialize and deserialize —
    // ASP.NET Core's own MVC JSON options don't apply here since this is a plain
    // HttpClient talking to a different process.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // --- Public catalogue -------------------------------------------------

    public async Task<List<Service>> GetServicesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/services", cancellationToken);
        return await ReadOrThrowAsync<List<Service>>(response, cancellationToken);
    }

    public async Task<List<Barber>> GetBarbersAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("/api/barbers", cancellationToken);
        return await ReadOrThrowAsync<List<Barber>>(response, cancellationToken);
    }

    public async Task<List<AvailableSlot>> GetAvailabilityAsync(
        DateOnly date, IReadOnlyCollection<string> serviceIds, string? barberId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder("/api/availability?date=")
            .Append(Uri.EscapeDataString(date.ToString("yyyy-MM-dd")));

        foreach (var serviceId in serviceIds)
        {
            query.Append("&serviceIds=").Append(Uri.EscapeDataString(serviceId));
        }

        if (!string.IsNullOrEmpty(barberId))
        {
            query.Append("&barberId=").Append(Uri.EscapeDataString(barberId));
        }

        using var response = await httpClient.GetAsync(query.ToString(), cancellationToken);
        return await ReadOrThrowAsync<List<AvailableSlot>>(response, cancellationToken);
    }

    // --- Bookings -----------------------------------------------------------

    public async Task<Booking> CreateBookingAsync(
        CreateBookingRequest request, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/bookings")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        httpRequest.Headers.Add("Idempotency-Key", idempotencyKey);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        return await ReadOrThrowAsync<Booking>(response, cancellationToken);
    }

    public async Task<List<Booking>> GetBookingsAsync(
        DateTimeOffset from, DateTimeOffset to, string? barberId = null,
        CancellationToken cancellationToken = default)
    {
        var query = new StringBuilder("/api/bookings?from=")
            .Append(Uri.EscapeDataString(from.ToString("O")))
            .Append("&to=")
            .Append(Uri.EscapeDataString(to.ToString("O")));

        if (!string.IsNullOrEmpty(barberId))
        {
            query.Append("&barberId=").Append(Uri.EscapeDataString(barberId));
        }

        using var response = await httpClient.GetAsync(query.ToString(), cancellationToken);
        return await ReadOrThrowAsync<List<Booking>>(response, cancellationToken);
    }

    public async Task<Booking> GetBookingAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(
            $"/api/bookings/{Uri.EscapeDataString(id)}", cancellationToken);
        return await ReadOrThrowAsync<Booking>(response, cancellationToken);
    }

    public async Task<Booking> RescheduleBookingAsync(
        string id, RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsync(
            $"/api/bookings/{Uri.EscapeDataString(id)}/reschedule",
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);
        return await ReadOrThrowAsync<Booking>(response, cancellationToken);
    }

    public async Task<Booking> CancelBookingAsync(
        string id, VersionRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"/api/bookings/{Uri.EscapeDataString(id)}/cancel", request, JsonOptions, cancellationToken);
        return await ReadOrThrowAsync<Booking>(response, cancellationToken);
    }

    public async Task<Booking> UpdateBookingStatusAsync(
        string id, StatusChangeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PatchAsync(
            $"/api/bookings/{Uri.EscapeDataString(id)}/status",
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);
        return await ReadOrThrowAsync<Booking>(response, cancellationToken);
    }

    // --- Barber time blocks ---------------------------------------------------

    public async Task<List<TimeBlock>> GetBarberBlocksAsync(
        string barberId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var query = $"/api/barbers/{Uri.EscapeDataString(barberId)}/blocks" +
                     $"?from={Uri.EscapeDataString(from.ToString("O"))}" +
                     $"&to={Uri.EscapeDataString(to.ToString("O"))}";

        using var response = await httpClient.GetAsync(query, cancellationToken);
        return await ReadOrThrowAsync<List<TimeBlock>>(response, cancellationToken);
    }

    public async Task<TimeBlock> CreateBarberBlockAsync(
        string barberId, CreateBlockRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"/api/barbers/{Uri.EscapeDataString(barberId)}/blocks", request, JsonOptions,
            cancellationToken);
        return await ReadOrThrowAsync<TimeBlock>(response, cancellationToken);
    }

    public async Task DeleteBarberBlockAsync(string blockId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(
            $"/api/blocks/{Uri.EscapeDataString(blockId)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NoContent || response.IsSuccessStatusCode)
        {
            return;
        }

        await ThrowForFailureAsync(response, cancellationToken);
    }

    // --- Shared response handling ---------------------------------------------

    private static async Task<T> ReadOrThrowAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForFailureAsync(response, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new KinsmenApiException(
            (int)response.StatusCode, problem: null,
            "The API returned an empty body where a result was expected.");
    }

    private static async Task ThrowForFailureAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // Auth failures and rate limiting can come back with no body at all — don't
        // assume every non-success response is JSON (API-CONTRACT.md, error table note).
        ApiProblem? problem = null;

        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ApiProblem>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // Body claimed to be JSON but wasn't parseable — fall through with problem null.
            }
        }

        var message = problem?.Detail ?? problem?.Title
            ?? $"The API returned {(int)response.StatusCode} {response.ReasonPhrase}.";

        throw new KinsmenApiException((int)response.StatusCode, problem, message);
    }
}
