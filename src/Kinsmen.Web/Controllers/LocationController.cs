using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

/// <summary>Fully static - address, hours and contact details, none of which come from
/// the API (there's no shop-level "opening hours" concept in the contract, only each
/// barber's own working periods). No API calls, so nothing here can fail the way every
/// other page can.</summary>
public sealed class LocationController : Controller
{
    public IActionResult Index() => View();
}
