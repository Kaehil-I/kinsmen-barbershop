using Kinsmen.Web.Helpers;
using Microsoft.AspNetCore.RateLimiting;
using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

public class AccountController : Controller
{
    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task Login(string returnUrl = "/")
    {
        var properties = new LoginAuthenticationPropertiesBuilder()
            .WithRedirectUri(SafeReturnUrl(returnUrl))
            .Build();
        await HttpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, properties);
    }

    [EnableRateLimiting(RateLimitPolicies.Login)]
    public async Task Register(string returnUrl = "/")
    {
        var properties = new LoginAuthenticationPropertiesBuilder()
            .WithRedirectUri(SafeReturnUrl(returnUrl))
            .WithParameter("screen_hint", "signup")
            .Build();
        await HttpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, properties);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task Logout()
    {
        var properties = new LogoutAuthenticationPropertiesBuilder()
            .WithRedirectUri(Url.Action("Index", "Home")!)
            .Build();
        await HttpContext.SignOutAsync(Auth0Constants.AuthenticationScheme, properties);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // Where a signed-in visitor lands when [Authorize(Roles = ...)] turns them away (the
    // cookie handler's default AccessDeniedPath) - without this action they got a 404.
    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    internal string SafeReturnUrl(string returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}