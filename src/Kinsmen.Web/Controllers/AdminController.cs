using Microsoft.AspNetCore.RateLimiting;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Admin catalogue: add, edit and (de)activate services and barbers. Talks to
/// /api/admin/* server-side, same proxy reasoning as the other controllers.
/// [Authorize(Roles = "Admin")] is the real gate here; the API re-checks the Admin role
/// on every one of these calls regardless, and enforces the business rules that matter
/// (unique active service names, one barber per login, no deactivating or shrinking the
/// hours of a barber with upcoming bookings) - this controller just passes its messages
/// through so the admin sees exactly why something was refused.</summary>
[Authorize(Roles = "Admin")]
public sealed class AdminController(IKinsmenApiClient apiClient) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            var servicesTask = apiClient.GetAdminServicesAsync(cancellationToken);
            var barbersTask = apiClient.GetAdminBarbersAsync(cancellationToken);
            await Task.WhenAll(servicesTask, barbersTask);

            return View(new AdminCataloguePageViewModel
            {
                Services = servicesTask.Result,
                Barbers = barbersTask.Result
            });
        }
        catch (KinsmenApiException ex)
        {
            return View(new AdminCataloguePageViewModel { ErrorMessage = ApiErrorMessages.For(ex) });
        }
        catch (HttpRequestException)
        {
            return View(new AdminCataloguePageViewModel { ErrorMessage = ApiErrorMessages.ForConnectionFailure() });
        }
    }

    // --- Services -----------------------------------------------------------

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SaveService(
        [FromBody] SaveServiceAjaxRequest request, CancellationToken cancellationToken)
    {
        var input = new ServiceInput
        {
            Name = request.Name,
            PriceCents = request.PriceCents,
            DurationMinutes = request.DurationMinutes
        };

        try
        {
            var saved = string.IsNullOrEmpty(request.Id)
                ? await apiClient.CreateAdminServiceAsync(input, cancellationToken)
                : await apiClient.UpdateAdminServiceAsync(request.Id, input, cancellationToken);

            return Json(new { success = true, id = saved.Id });
        }
        catch (KinsmenApiException ex) { return Failure(ex); }
        catch (HttpRequestException) { return ConnectionFailure(); }
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SetServiceActive(
        string id, [FromBody] ActiveInput request, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await apiClient.SetAdminServiceActiveAsync(id, request.Active, cancellationToken);
            return Json(new { success = true, id = saved.Id, active = saved.Active });
        }
        catch (KinsmenApiException ex) { return Failure(ex); }
        catch (HttpRequestException) { return ConnectionFailure(); }
    }

    // --- Barbers ------------------------------------------------------------

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SaveBarber(
        [FromBody] SaveBarberAjaxRequest request, CancellationToken cancellationToken)
    {
        var input = new BarberInput
        {
            Name = request.Name,
            UserId = request.UserId,
            Hours = request.Hours,
            ServiceIds = request.ServiceIds
        };

        try
        {
            var saved = string.IsNullOrEmpty(request.Id)
                ? await apiClient.CreateAdminBarberAsync(input, cancellationToken)
                : await apiClient.UpdateAdminBarberAsync(request.Id, input, cancellationToken);

            return Json(new { success = true, id = saved.Id });
        }
        catch (KinsmenApiException ex) { return Failure(ex); }
        catch (HttpRequestException) { return ConnectionFailure(); }
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SetBarberActive(
        string id, [FromBody] ActiveInput request, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await apiClient.SetAdminBarberActiveAsync(id, request.Active, cancellationToken);
            return Json(new { success = true, id = saved.Id, active = saved.Active });
        }
        catch (KinsmenApiException ex) { return Failure(ex); }
        catch (HttpRequestException) { return ConnectionFailure(); }
    }

    // The API's own message is what the admin needs to see for the refusals that matter
    // here (duplicate name, duplicate login, upcoming bookings) - ApiErrorMessages passes
    // 400s and 409s straight through and only rewrites the generic auth/server codes.
    private IActionResult Failure(KinsmenApiException ex) =>
        StatusCode(ex.StatusCode, new { errorCode = ex.ErrorCode, message = ApiErrorMessages.For(ex) });

    private IActionResult ConnectionFailure() =>
        StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
}
