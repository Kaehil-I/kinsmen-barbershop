using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Serves the booking page and the two AJAX endpoints it calls
/// (Availability, Create). Runs server-side rather than having the page's JS call
/// src/Kinsmen.Api directly, for two reasons: the API's CORS isn't opened to arbitrary
/// origins (API-CONTRACT.md, "Error handling for Greg"), and this keeps the bearer
/// token out of browser-visible code entirely — BearerTokenHandler attaches it
/// server-side via the same IKinsmenApiClient every other controller uses.</summary>
public sealed class BookingController(IKinsmenApiClient apiClient) : Controller
{
    // Africa/Johannesburg has no DST, so this offset is always correct — but resolve
    // it properly rather than hardcoding, in case that ever changes.
    private static readonly TimeZoneInfo ShopTimeZone = ResolveShopTimeZone();

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var services = await apiClient.GetServicesAsync(cancellationToken);
            var barbers = await apiClient.GetBarbersAsync(cancellationToken);
            return View(new BookingPageViewModel { Services = services, Barbers = barbers });
        }
        catch (Exception ex) when (ex is KinsmenApiException or HttpRequestException)
        {
            // See ServicesController — same reasoning, full handling in step 8.
            return View(new BookingPageViewModel { ApiUnavailable = true });
        }
    }

    /// <summary>JSON endpoint the booking page's JS calls whenever the customer's
    /// barber/services/date selection changes. Returns shop-local time strings so the
    /// page never has to do timezone math itself — just startUtc to echo back on submit.</summary>
    [HttpGet]
    public async Task<IActionResult> Availability(
        [FromQuery] DateOnly date,
        [FromQuery(Name = "serviceIds")] List<string> serviceIds,
        [FromQuery] string? barberId,
        CancellationToken cancellationToken)
    {
        if (serviceIds.Count == 0)
        {
            return BadRequest(new { message = "Pick at least one service." });
        }

        List<AvailableSlot> slots;

        try
        {
            slots = await apiClient.GetAvailabilityAsync(
                date, serviceIds, string.IsNullOrEmpty(barberId) ? null : barberId, cancellationToken);
        }
        catch (KinsmenApiException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Couldn't reach the booking service." });
        }

        // With "any available barber" selected, different barbers can open up the same
        // start time — the customer only cares that a slot exists, not which barber's
        // calendar it came from (the create call still passes barberId through as
        // whatever the customer chose, so the API resolves that assignment itself).
        var distinctSlots = slots
            .GroupBy(s => s.StartUtc)
            .Select(g => g.First())
            .OrderBy(s => s.StartUtc)
            .Select(s => new
            {
                time = TimeZoneInfo.ConvertTime(s.StartUtc, ShopTimeZone).ToString("HH:mm"),
                startUtc = s.StartUtc.ToString("O")
            });

        return Json(distinctSlots);
    }

    /// <summary>JSON endpoint the booking page's JS posts to when the customer confirms.
    /// Expects an Idempotency-Key header — see API-CONTRACT.md "Safe retries" and
    /// wwwroot/js/booking.js for how the key's lifetime is managed client-side.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateBookingAjaxRequest request, CancellationToken cancellationToken)
    {
        if (!Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyHeader)
            || string.IsNullOrWhiteSpace(idempotencyKeyHeader))
        {
            return BadRequest(new { message = "Missing Idempotency-Key." });
        }

        string idempotencyKey = idempotencyKeyHeader.ToString();

        if (!DateTimeOffset.TryParse(request.StartUtc, out var start))
        {
            return BadRequest(new { message = "Invalid start time." });
        }

        var apiRequest = new CreateBookingRequest
        {
            BarberId = string.IsNullOrEmpty(request.BarberId) ? null : request.BarberId,
            ServiceIds = request.ServiceIds,
            Start = start
        };

        try
        {
            var booking = await apiClient.CreateBookingAsync(apiRequest, idempotencyKey, cancellationToken);
            return Json(new
            {
                success = true,
                bookingId = booking.Id,
                barberId = booking.BarberId,
                startUtc = booking.StartUtc.ToString("O"),
                totalCents = booking.TotalCents
            });
        }
        catch (KinsmenApiException ex)
        {
            // 409 booking_conflict is the one this step is specifically about — surface
            // it plainly so the page can tell the customer to pick another slot. Every
            // other code still comes through with its message; the full per-code UI
            // treatment across all screens is step 8.
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Couldn't reach the booking service." });
        }
    }

    private static TimeZoneInfo ResolveShopTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
        }
    }
}
