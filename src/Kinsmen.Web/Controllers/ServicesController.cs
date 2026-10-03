using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

public sealed class ServicesController(IKinsmenApiClient apiClient) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        List<Service> services;

        try
        {
            services = await apiClient.GetServicesAsync(cancellationToken);
        }
        catch (KinsmenApiException ex)
        {
            ViewData["ErrorMessage"] = ApiErrorMessages.For(ex);
            return View(new List<Service>());
        }
        catch (HttpRequestException)
        {
            ViewData["ErrorMessage"] = ApiErrorMessages.ForConnectionFailure();
            return View(new List<Service>());
        }

        return View(services);
    }
}
