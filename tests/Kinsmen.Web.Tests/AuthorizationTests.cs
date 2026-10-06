using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Kinsmen.Web.Tests;

public sealed class Auth0FactAttribute : FactAttribute
{
    public Auth0FactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KINSMEN_TEST_AUTH0")))
            Skip = "Set KINSMEN_TEST_AUTH0=1 to run tests that need a live round trip to Auth0.";
    }
}

public sealed class WebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Real, non-secret values from appsettings.json — the client secret isn't needed
        // just to redirect an anonymous visitor to Auth0's login page.
        builder.UseSetting("Auth0:Domain", "kinsmen.eu.auth0.com");
        builder.UseSetting("Auth0:ClientId", "iFR7Xzx8OIh0HWrUCjScifbOLr4vOdvJ");
        builder.UseSetting("Auth0:ClientSecret", "test-placeholder-not-used-for-this-check");
        builder.UseSetting("Auth0:Audience", "kinsmen-api");
    }
}

public sealed class AuthorizationTests : IClassFixture<WebFactory>
{
    private readonly WebFactory factory;
    public AuthorizationTests(WebFactory factory) => this.factory = factory;

    [Auth0Fact]
    public async Task AnonymousVisitorToMyBookingsIsRedirectedToAuth0Login()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/MyBookings");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://kinsmen.eu.auth0.com/authorize", response.Headers.Location!.ToString());
    }

    [Auth0Fact]
    public async Task PublicBookingBrowsingStaysOpenToAnonymousVisitors()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/Booking");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}