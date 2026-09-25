using Kinsmen.Web.ApiClient;
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
        catch (Exception ex) when (ex is KinsmenApiException or HttpRequestException)
        {
            // Full per-status-code handling lands in the cross-cutting error pass
            // (step 8) — for now, fail gracefully rather than show a raw exception
            // page while Zario's API may not be running locally.
            ViewData["ApiUnavailable"] = true;
            return View(new List<Service>());
        }

        return View(services);
    }
}
