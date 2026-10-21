using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Public retail catalogue. Purchases remain in-store until checkout is designed.</summary>
public sealed class ShopController(IKinsmenApiClient apiClient) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            return View(await apiClient.GetProductsAsync(cancellationToken));
        }
        catch (KinsmenApiException ex)
        {
            ViewData["ErrorMessage"] = ApiErrorMessages.For(ex);
            return View(new List<RetailProduct>());
        }
        catch (HttpRequestException)
        {
            ViewData["ErrorMessage"] = ApiErrorMessages.ForConnectionFailure();
            return View(new List<RetailProduct>());
        }
    }
}
