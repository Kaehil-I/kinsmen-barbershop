using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

public class AccountController : Controller
{
    public async Task Login(string returnUrl = "/")
    {
        var properties = new LoginAuthenticationPropertiesBuilder()
            .WithRedirectUri(SafeReturnUrl(returnUrl))
            .Build();
        await HttpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, properties);
    }

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

    private string SafeReturnUrl(string returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}