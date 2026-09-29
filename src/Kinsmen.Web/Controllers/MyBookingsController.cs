using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Serves the customer's own-bookings page plus its AJAX actions
/// (RescheduleAvailability, Reschedule, Cancel, SubmitReview). Same reasoning as BookingController
/// for why this proxies through the server rather than the page's JS calling
/// src/Kinsmen.Api directly (CORS, keeping the bearer token server-side).</summary>

[Authorize]
public sealed class MyBookingsController(IKinsmenApiClient apiClient) : Controller
{
    private static readonly TimeZoneInfo ShopTimeZone = ShopTimeZoneProvider.Instance;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;

            var upcoming = await apiClient.GetBookingsAsync(now, now.AddDays(31), barberId: null, cancellationToken);
            // A finished visit's own start time is in the past, so it falls outside the window
            // above. Ask for the last 30 days too, but only keep Completed bookings from it -
            // that's the one status a customer can still act on (leaving a review) after the fact.
            var past = await apiClient.GetBookingsAsync(now.AddDays(-30), now, barberId: null, cancellationToken);
            var bookings = upcoming
                .Concat(past.Where(b => b.Status == BookingStatus.Completed))
                .GroupBy(b => b.Id).Select(g => g.First())
                .OrderBy(b => b.StartUtc)
                .ToList();

            var barbers = await apiClient.GetBarbersAsync(cancellationToken);

            return View(new MyBookingsPageViewModel
            {
                Bookings = bookings,
                BarberNamesById = barbers.ToDictionary(b => b.Id, b => b.Name)
            });
        }
        catch (Exception ex) when (ex is KinsmenApiException or HttpRequestException)
        {
            // See ServicesController - same reasoning, full handling in step 8.
            return View(new MyBookingsPageViewModel { ApiUnavailable = true });
        }
    }

    /// <summary>JSON endpoint for the inline reschedule panel. A reschedule can only move
    /// the time - same barber, same services (API-CONTRACT.md: "Changing barber or
    /// selected services during rescheduling is not part of this API version") - so this
    /// looks the booking up first to check availability against its existing barber and
    /// services, not ones the customer could pick fresh.</summary>
    [HttpGet]
    public async Task<IActionResult> RescheduleAvailability(
        string id, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await apiClient.GetBookingAsync(id, cancellationToken);
            var serviceIds = booking.Services.Select(s => s.ServiceId).ToList();

            var slots = await apiClient.GetAvailabilityAsync(date, serviceIds, booking.BarberId, cancellationToken);

            var result = slots
                .OrderBy(s => s.StartUtc)
                .Select(s => new
                {
                    time = TimeZoneInfo.ConvertTime(s.StartUtc, ShopTimeZone).ToString("HH:mm"),
                    startUtc = s.StartUtc.ToString("O")
                });

            return Json(result);
        }
        catch (KinsmenApiException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Couldn't reach the booking service." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Reschedule(
        string id, [FromBody] RescheduleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await apiClient.RescheduleBookingAsync(id, request, cancellationToken);
            return Json(ToJson(booking));
        }
        catch (KinsmenApiException ex)
        {
            // 409 stale_version is the one this step is specifically about - the
            // message already says to refresh, which the client-side handler acts on.
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Couldn't reach the booking service." });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Cancel(
        string id, [FromBody] VersionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await apiClient.CancelBookingAsync(id, request, cancellationToken);
            return Json(ToJson(booking));
        }
        catch (KinsmenApiException ex)
        {
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Couldn't reach the booking service." });
        }
    }

    /// <summary>Customer-only, once per booking, and only after the API confirms it's
    /// Completed - all enforced server-side in BookingService.SubmitReview, not here.</summary>
    [HttpPost]
    public async Task<IActionResult> SubmitReview(
        string id, [FromBody] SubmitReviewRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.SubmitReviewAsync(id, request, cancellationToken);
            return Json(new { success = true });
        }
        catch (KinsmenApiException ex)
        {
            // 409 covers "not completed yet" and "already reviewed" - the message is safe to show as-is.
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ex.Message });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = "Couldn't reach the booking service." });
        }
    }

    private static object ToJson(Booking booking) => new
    {
        success = true,
        id = booking.Id,
        startUtc = booking.StartUtc.ToString("O"),
        status = booking.Status.ToString(),
        version = booking.Version,
        time = TimeZoneInfo.ConvertTime(booking.StartUtc, ShopTimeZone).ToString("HH:mm"),
        date = TimeZoneInfo.ConvertTime(booking.StartUtc, ShopTimeZone).ToString("ddd d MMM")
    };
}