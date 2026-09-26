using Kinsmen.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;

namespace Kinsmen.Web.Tests;

public sealed class AccountControllerTests
{
    // Url.IsLocalUrl needs a real HttpContext (scheme + host) to judge whether an
    // absolute URL happens to point back at this same site, so this wires up just
    // enough of a fake request to exercise that logic for real — no server, no network.
    private static AccountController MakeController()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost", 7090);
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new AccountController { Url = new UrlHelper(actionContext) };
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/MyBookings")]
    [InlineData("/Booking/Index?date=2026-10-01")]
    public void LocalPathsArePassedThrough(string returnUrl)
        => Assert.Equal(returnUrl, MakeController().SafeReturnUrl(returnUrl));

    [Theory]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com/phishing")]
    [InlineData("//evil.com")]
    [InlineData("https://localhost:7090.evil.com")]
    public void ExternalOrProtocolRelativeUrlsAreRejected(string returnUrl)
        => Assert.Equal("/", MakeController().SafeReturnUrl(returnUrl));

    [Fact]
    public void EmptyReturnUrlFallsBackToHome()
        => Assert.Equal("/", MakeController().SafeReturnUrl(""));

    [Fact]
    public void SameHostAbsoluteUrlIsRejected()
     => Assert.Equal("/", MakeController().SafeReturnUrl("https://localhost:7090/MyBookings"));
}