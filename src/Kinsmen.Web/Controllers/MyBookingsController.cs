using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Serves the customer's own-bookings page plus its two AJAX actions
/// (RescheduleAvailability, Reschedule, Cancel). Same reasoning as BookingController
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
            var from = DateTimeOffset.UtcNow;
            var to = from.AddDays(31);

            var bookings = await apiClient.GetBookingsAsync(from, to, barberId: null, cancellationToken);
            var barbers = await apiClient.GetBarbersAsync(cancellationToken);

            return View(new MyBookingsPageViewModel
            {
                Bookings = [.. bookings.OrderBy(b => b.StartUtc)],
                BarberNamesById = barbers.ToDictionary(b => b.Id, b => b.Name)
            });
        }
        catch (KinsmenApiException ex)
        {
            return View(new MyBookingsPageViewModel { ErrorMessage = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return View(new MyBookingsPageViewModel { ErrorMessage = ApiErrorMessages.ForConnectionFailure() });
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
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
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
            // 409 stale_version gets its own message in my-bookings.js (result.body
            // still carries this one as a fallback for any other status code that
            // reaches this same catch).
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
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
            return StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
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
