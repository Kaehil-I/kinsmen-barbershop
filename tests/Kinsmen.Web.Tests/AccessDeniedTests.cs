using Kinsmen.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Tests;

public sealed class AccessDeniedTests
{
    // A signed-in visitor turned away by [Authorize(Roles = ...)] is sent to
    // /Account/AccessDenied. That page used to not exist (a 404), so the person had no
    // idea whether the site was broken or they simply weren't allowed in. It should
    // render a real page, and report 403 rather than pretending the request succeeded.
    [Fact]
    public void AccessDeniedShowsAPageWithA403Status()
    {
        var httpContext = new DefaultHttpContext();
        var controller = new AccountController
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = controller.AccessDenied();

        Assert.IsType<ViewResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);
    }
}
