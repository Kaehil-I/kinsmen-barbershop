using System.Security.Claims;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Kinsmen.Web.Controllers;

/// <summary>A customer's profile: display name, phone number and preferred barber, plus their recent
/// visits with "Book again". The API enforces every rule (customer only, own profile only, phone format,
/// active barber) and its messages are shown as-is. A plain form post, so it works without JavaScript.</summary>
[Authorize]
public sealed class ProfileController(IProfileApiClient profiles, IKinsmenApiClient apiClient) : Controller
{
    // The API lists at most 31 days per request, so history is fetched in three 30-day windows.
    private const int HistoryWindows = 3;
    private const int WindowDays = 30;

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // Staff have no customer profile (the API refuses them), so send them to their own screen.
        if (User.IsStaff()) return RedirectToAction("Index", "BarberSchedule");

        var model = new ProfilePageViewModel { Saved = TempData["ProfileSaved"] is true };
        try
        {
            var profile = await profiles.GetAsync(cancellationToken);
            model.Form = new ProfileForm
            {
                DisplayName = profile.DisplayName,
                Phone = PhoneFormatter.Local(profile.Phone),
                PreferredBarberId = profile.PreferredBarberId
            };
        }
        catch (KinsmenApiException ex) { model.ErrorMessage = ApiErrorMessages.For(ex); }
        catch (HttpRequestException) { model.ErrorMessage = ApiErrorMessages.ForConnectionFailure(); }

        await LoadPageData(model, cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Writes)]
    public async Task<IActionResult> Save(ProfileForm form, CancellationToken cancellationToken)
    {
        if (User.IsStaff()) return RedirectToAction("Index", "BarberSchedule");

        try
        {
            await profiles.SaveAsync(new ProfileInput
            {
                DisplayName = form.DisplayName,
                Phone = form.Phone,
                PreferredBarberId = form.PreferredBarberId
            }, cancellationToken);

            // Post/redirect/get, so refreshing the page doesn't resubmit the form.
            TempData["ProfileSaved"] = true;
            return RedirectToAction(nameof(Index));
        }
        catch (KinsmenApiException ex)
        {
            return await RedisplayWithError(form, ApiErrorMessages.For(ex), cancellationToken);
        }
        catch (HttpRequestException)
        {
            return await RedisplayWithError(form, ApiErrorMessages.ForConnectionFailure(), cancellationToken);
        }
    }

    private async Task<IActionResult> RedisplayWithError(ProfileForm form, string message, CancellationToken cancellationToken)
    {
        var model = new ProfilePageViewModel { Form = form, SaveError = message };
        await LoadPageData(model, cancellationToken);
        return View("Index", model);
    }

    // Barbers (for the preferred-barber list and history names) and recent visits. Failures here
    // only affect the history, never the form.
    private async Task LoadPageData(ProfilePageViewModel model, CancellationToken cancellationToken)
    {
        model.Email = User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
        try
        {
            model.Barbers = await apiClient.GetBarbersAsync(cancellationToken);
            model.BarberNamesById = model.Barbers.ToDictionary(b => b.Id, b => b.Name);

            var now = DateTimeOffset.UtcNow;
            var history = new List<Booking>();
            for (var i = 0; i < HistoryWindows; i++)
            {
                var to = now.AddDays(-WindowDays * i);
                history.AddRange(await apiClient.GetBookingsAsync(to.AddDays(-WindowDays), to, barberId: null, cancellationToken));
            }
            model.History = history
                .Where(b => b.StartUtc < now)
                .GroupBy(b => b.Id).Select(g => g.First())
                .OrderByDescending(b => b.StartUtc)
                .ToList();
        }
        catch (KinsmenApiException ex) { model.HistoryError = ApiErrorMessages.For(ex); }
        catch (HttpRequestException) { model.HistoryError = ApiErrorMessages.ForConnectionFailure(); }
    }
}
