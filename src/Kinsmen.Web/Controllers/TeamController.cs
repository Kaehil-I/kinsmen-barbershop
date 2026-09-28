using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
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
        catch (KinsmenApiException ex)
        {
            ViewData["ErrorMessage"] = ApiErrorMessages.For(ex);
            return View(new List<Barber>());
        }
        catch (HttpRequestException)
        {
            ViewData["ErrorMessage"] = ApiErrorMessages.ForConnectionFailure();
            return View(new List<Barber>());
        }

        return View(barbers);
    }
}
