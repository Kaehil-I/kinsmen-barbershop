using System.Security.Claims;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kinsmen.Web.Controllers;

/// <summary>Staff accounts: admins find people by email and change their role (Customer, Barber,
/// Admin). The API enforces every rule (admin only, verified email, no changing your own role, no
/// demoting admins, no demoting a barber who still has bookings) and records each change; this
/// controller passes its messages straight through so the admin sees exactly why something was refused.</summary>
[Authorize(Roles = "Admin")]
public sealed class StaffController(IStaffApiClient staffApi) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = new StaffPageViewModel
        {
            CurrentUserId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value
        };
        try
        {
            model.Enabled = (await staffApi.GetStatusAsync(cancellationToken)).Enabled;
            model.RecentChanges = await staffApi.GetAuditAsync(cancellationToken);
            if (model.Enabled) model.Staff = await staffApi.GetStaffAsync(cancellationToken);
        }
        catch (KinsmenApiException ex) { model.ErrorMessage = ApiErrorMessages.For(ex); }
        catch (HttpRequestException) { model.ErrorMessage = ApiErrorMessages.ForConnectionFailure(); }
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Lookup(string email, CancellationToken cancellationToken)
    {
        try { return Json(await staffApi.LookupAsync(email ?? string.Empty, cancellationToken)); }
        catch (KinsmenApiException ex) { return Failure(ex); }
        catch (HttpRequestException) { return ConnectionFailure(); }
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> SetRole([FromBody] SetRoleAjaxRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await staffApi.SetRoleAsync(request.UserId, request.Role, cancellationToken);
            return Json(new { success = true, userId = updated.UserId, role = updated.Role });
        }
        catch (KinsmenApiException ex) { return Failure(ex); }
        catch (HttpRequestException) { return ConnectionFailure(); }
    }

    // Auth0 outages and "not switched on" come back as 5xx, which the shared helper would describe
    // as a booking-service fault; the API's own message is accurate for these, so it's shown instead.
    private IActionResult Failure(KinsmenApiException ex) =>
        StatusCode(ex.StatusCode, new
        {
            errorCode = ex.ErrorCode,
            message = ex.ErrorCode is { } code && (code.StartsWith("identity_provider_") || code == "staff_management_disabled")
                ? ex.Message
                : ApiErrorMessages.For(ex)
        });

    private IActionResult ConnectionFailure() =>
        StatusCode(503, new { message = ApiErrorMessages.ForConnectionFailure() });
}
