using Microsoft.AspNetCore.RateLimiting;
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
public sealed class BookingController(IKinsmenApiClient apiClient, IProfileApiClient profiles) : Controller
{
    private static readonly TimeZoneInfo ShopTimeZone = ShopTimeZoneProvider.Instance;

    public async Task<IActionResult> Index(
        [FromQuery] string? serviceId,
        [FromQuery(Name = "serviceIds")] List<string>? serviceIds,
        [FromQuery] string? barberId,
        [FromQuery] string? productId,
        CancellationToken cancellationToken)
    {
        // Staff don't book appointments (a barber's booking attempt is refused outright, and
        // an admin has no customer to book for), so this page is no use to them.
        if (User.IsStaff()) return RedirectToAction("Index", "BarberSchedule");

        try
        {
            var services = await apiClient.GetServicesAsync(cancellationToken);
            var barbers = await apiClient.GetBarbersAsync(cancellationToken);
            var products = await apiClient.GetProductsAsync(cancellationToken);

            // ?serviceId= (single, from Services/Team) and ?serviceIds= (several, from
            // "Book again") both feed the same list.
            var requestedServiceIds = (serviceIds ?? [])
                .Append(serviceId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct()
                .ToList();

            // Only honour ids that match something currently offered - an old or
            // tampered-with link shouldn't point at a service/barber that's gone.
            var keptServiceIds = requestedServiceIds.Where(id => services.Any(s => s.Id == id)).ToList();

            // No barber in the link: start with the customer's preferred barber, if they've set one.
            var linkNamedBarber = !string.IsNullOrEmpty(barberId);
            if (!linkNamedBarber) barberId = await PreferredBarberId(cancellationToken);
            var barberStillOffered = barbers.Any(b => b.Id == barberId);

            return View(new BookingPageViewModel
            {
                Services = services,
                Barbers = barbers,
                Products = products,
                PreselectedServiceIds = keptServiceIds,
                PreselectedBarberId = barberStillOffered ? barberId : null,
                // Same rule as services and barbers: a stale or tampered-with link can't preselect
                // a product that is gone or sold out.
                PreselectedProductId = products.Any(p => p.Id == productId && p.StockQuantity > 0) ? productId : null,
                RebookNotice = RebookNoticeFor(
                    droppedServices: requestedServiceIds.Count - keptServiceIds.Count,
                    barberDropped: linkNamedBarber && !barberStillOffered)
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

    // Best effort: the booking page must still work if the profile can't be read, and anonymous
    // visitors have no profile at all.
    private async Task<string?> PreferredBarberId(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return null;
        try { return (await profiles.GetAsync(cancellationToken)).PreferredBarberId; }
        catch (KinsmenApiException) { return null; }
        catch (HttpRequestException) { return null; }
    }

    private static string? RebookNoticeFor(int droppedServices, bool barberDropped)
    {
        if (droppedServices == 0 && !barberDropped) return null;
        var parts = new List<string>();
        if (droppedServices > 0)
            parts.Add(droppedServices == 1 ? "one of those services isn't offered any more" : "some of those services aren't offered any more");
        if (barberDropped) parts.Add("that barber isn't taking bookings right now");
        var sentence = string.Join(", and ", parts);
        return char.ToUpperInvariant(sentence[0]) + sentence[1..] + " \u2014 pick a replacement below.";
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
    [EnableRateLimiting(RateLimitPolicies.Writes)]
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
            Products = request.Products,
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
