using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Serves the booking page and the two AJAX endpoints it calls
/// (Availability, Create). Runs server-side rather than having the page's JS call
/// src/Kinsmen.Api directly, for two reasons: the API's CORS isn't opened to arbitrary
/// origins (API-CONTRACT.md, "Error handling for Greg"), and this keeps the bearer
/// token out of browser-visible code entirely - BearerTokenHandler attaches it
/// server-side via the same IKinsmenApiClient every other controller uses.</summary>
public sealed class BookingController(IKinsmenApiClient apiClient) : Controller
{
    private static readonly TimeZoneInfo ShopTimeZone = ShopTimeZoneProvider.Instance;

    public async Task<IActionResult> Index(
        [FromQuery] string? serviceId, [FromQuery] string? barberId, CancellationToken cancellationToken)
    {
        try
        {
            var services = await apiClient.GetServicesAsync(cancellationToken);
            var barbers = await apiClient.GetBarbersAsync(cancellationToken);

            return View(new BookingPageViewModel
            {
                Services = services,
                Barbers = barbers,
                // Only honour these if they actually match something real - an old or
                // tampered-with link shouldn't silently point at a service/barber that
                // no longer exists.
                PreselectedServiceId = services.Any(s => s.Id == serviceId) ? serviceId : null,
                PreselectedBarberId = barbers.Any(b => b.Id == barberId) ? barberId : null
            });
        }
        catch (KinsmenApiException ex)
        {
            return View(new BookingPageViewModel { ErrorMessage = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return View(new BookingPageViewModel { ErrorMessage = ApiErrorMessages.ForConnectionFailure() });
        }
    }

    /// <summary>JSON endpoint the booking page's JS calls whenever the customer's
    /// barber/services/date selection changes. Returns shop-local time strings so the
    /// page never has to do timezone math itself - just startUtc to echo back on submit.</summary>
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
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
        }

        // With "any available barber" selected, different barbers can open up the same
        // start time - the customer only cares that a slot exists, not which barber's
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
    /// Expects an Idempotency-Key header - see API-CONTRACT.md "Safe retries" and
    /// wwwroot/js/booking.js for how the key's lifetime is managed client-side.</summary>
    [HttpPost]
    [Authorize]
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
            Start = start,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes
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
            // 409 booking_conflict gets its own message in booking.js (result.body
            // still carries this one as a fallback for any other status code that
            // reaches this same catch). Every other code now maps through
            // ApiErrorMessages for consistent wording with the rest of the app.
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
        }
    }
}
