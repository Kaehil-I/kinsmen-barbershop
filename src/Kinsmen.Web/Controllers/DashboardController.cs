using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>One page for admins to see how the business is doing: bookings by outcome, revenue, the most booked
/// services and which products are running low. The API works out the numbers and enforces the Admin role; this
/// only picks the dates (this month unless the filter says otherwise) and shows the result.</summary>
[Authorize(Roles = "Admin")]
public sealed class DashboardController(IDashboardApiClient api, TimeProvider clock) : Controller
{
    public async Task<IActionResult> Index(string? preset, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), ShopTimeZoneProvider.Instance).DateTime);
        var model = new DashboardPageViewModel { Period = DashboardPeriod.Resolve(preset, from, to, today) };
        try { model.Report = await api.GetAsync(model.Period.From, model.Period.To, cancellationToken); }
        catch (KinsmenApiException ex) { model.ErrorMessage = ApiErrorMessages.For(ex); }
        catch (HttpRequestException) { model.ErrorMessage = ApiErrorMessages.ForConnectionFailure(); }
        return View(model);
    }
}
