using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Serves the barber's own-schedule page plus its AJAX actions
/// (UpdateStatus, CreateBlock, DeleteBlock, BlockAvailability). Same server-proxy
/// reasoning as BookingController/MyBookingsController throughout.</summary>
public sealed class BarberScheduleController(IKinsmenApiClient apiClient) : Controller
{
    private static readonly TimeZoneInfo ShopTimeZone = ShopTimeZoneProvider.Instance;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var from = DateTimeOffset.UtcNow;
            var to = from.AddDays(31);

            // GET /api/bookings auto-scopes to "own schedule" for a Barber token
            // (API-CONTRACT.md), so no barberId filter is passed here.
            var bookings = await apiClient.GetBookingsAsync(from, to, barberId: null, cancellationToken);
            var ordered = bookings.OrderBy(b => b.StartUtc).ToList();

            // The API has no endpoint that maps a staff token straight to its public
            // barberId (only the internal, undocumented "staff subject -> barber.userId"
            // mapping Zario's own docs mention) - so this infers it from an existing
            // booking. If the barber has none in the next 31 days, time-block management
            // can't be shown; the view explains this rather than silently doing nothing.
            var myBarberId = ordered.FirstOrDefault()?.BarberId;

            List<TimeBlock> blocks = [];
            if (myBarberId is not null)
            {
                blocks = await apiClient.GetBarberBlocksAsync(myBarberId, from, to, cancellationToken);
            }

            return View(new BarberSchedulePageViewModel
            {
                Bookings = ordered,
                Blocks = [.. blocks.OrderBy(b => b.StartUtc)],
                MyBarberId = myBarberId
            });
        }
        catch (KinsmenApiException ex)
        {
            return View(new BarberSchedulePageViewModel { ErrorMessage = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return View(new BarberSchedulePageViewModel { ErrorMessage = ApiErrorMessages.ForConnectionFailure() });
        }
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStatus(
        string id, [FromBody] StatusChangeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var booking = await apiClient.UpdateBookingStatusAsync(id, request, cancellationToken);
            return Json(new
            {
                success = true,
                id = booking.Id,
                status = booking.Status.ToString(),
                version = booking.Version
            });
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

    /// <summary>JSON endpoint the time-block form calls when the barber picks a date.
    /// Returns the barber's working hours for that weekday plus every already-busy
    /// period (non-cancelled bookings and existing blocks) as shop-local "HH:mm" pairs,
    /// so the page's JS can render a slot grid - same style as the booking flow's time
    /// picker - showing only genuinely free time, instead of a blank time input the
    /// barber could accidentally point at an already-booked slot.</summary>
    [HttpGet]
    public async Task<IActionResult> BlockAvailability(
        [FromQuery] string barberId, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        try
        {
            var localMidnight = date.ToDateTime(TimeOnly.MinValue);
            var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localMidnight, ShopTimeZone);
            var from = new DateTimeOffset(dayStartUtc, TimeSpan.Zero);
            var to = from.AddDays(1);

            var barbersTask = apiClient.GetBarbersAsync(cancellationToken);
            var bookingsTask = apiClient.GetBookingsAsync(from, to, barberId, cancellationToken);
            var blocksTask = apiClient.GetBarberBlocksAsync(barberId, from, to, cancellationToken);
            await Task.WhenAll(barbersTask, bookingsTask, blocksTask);

            var barber = barbersTask.Result.FirstOrDefault(b => b.Id == barberId);

            // ISO weekday (Monday = 1 ... Sunday = 7), matching WorkingPeriod.Weekday -
            // .NET's own DayOfWeek numbers Sunday = 0, so this needs converting.
            var isoWeekday = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            var workingPeriod = barber?.Hours.FirstOrDefault(h => h.Weekday == isoWeekday);

            var busyPeriods = bookingsTask.Result
                .Where(b => b.Status != BookingStatus.Cancelled)
                .Select(b => (Start: b.StartUtc, End: b.EndUtc))
                .Concat(blocksTask.Result.Select(bl => (Start: bl.StartUtc, End: bl.EndUtc)))
                .OrderBy(p => p.Start)
                .Select(p => new
                {
                    start = TimeZoneInfo.ConvertTime(p.Start, ShopTimeZone).ToString("HH:mm"),
                    end = TimeZoneInfo.ConvertTime(p.End, ShopTimeZone).ToString("HH:mm")
                });

            return Json(new
            {
                workingStart = workingPeriod is null ? null : MinutesToTime(workingPeriod.StartMinute),
                workingEnd = workingPeriod is null ? null : MinutesToTime(workingPeriod.EndMinute),
                busyPeriods
            });
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
    public async Task<IActionResult> CreateBlock(
        [FromQuery] string barberId, [FromBody] CreateBlockRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var block = await apiClient.CreateBarberBlockAsync(barberId, request, cancellationToken);
            return Json(new
            {
                success = true,
                id = block.Id,
                startUtc = block.StartUtc.ToString("O"),
                endUtc = block.EndUtc.ToString("O"),
                reason = block.Reason,
                dateLabel = TimeZoneInfo.ConvertTime(block.StartUtc, ShopTimeZone).ToString("ddd d MMM"),
                timeLabel = TimeZoneInfo.ConvertTime(block.StartUtc, ShopTimeZone).ToString("HH:mm") + "\u2013" +
                            TimeZoneInfo.ConvertTime(block.EndUtc, ShopTimeZone).ToString("HH:mm")
            });
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
    public async Task<IActionResult> DeleteBlock(string id, CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.DeleteBarberBlockAsync(id, cancellationToken);
            return Json(new { success = true });
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

    private static string MinutesToTime(int minutesSinceMidnight) =>
        TimeSpan.FromMinutes(minutesSinceMidnight).ToString(@"hh\:mm");
}
