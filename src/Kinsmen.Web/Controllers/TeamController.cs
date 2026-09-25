using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

public sealed class TeamController(IKinsmenApiClient apiClient) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        List<Barber> barbers;

        try
        {
            barbers = await apiClient.GetBarbersAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is KinsmenApiException or HttpRequestException)
        {
            // See ServicesController — same reasoning, full handling in step 8.
            ViewData["ApiUnavailable"] = true;
            return View(new List<Barber>());
        }

        return View(barbers);
    }
}
