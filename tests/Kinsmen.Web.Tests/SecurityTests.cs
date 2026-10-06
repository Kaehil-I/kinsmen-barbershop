using System.Net;
using System.Reflection;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Controllers;
using Kinsmen.Web.Helpers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Kinsmen.Web.Tests;

public sealed class LimitedWebFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Auth0:Domain", "kinsmen.eu.auth0.com");
        builder.UseSetting("Auth0:ClientId", "iFR7Xzx8OIh0HWrUCjScifbOLr4vOdvJ");
        builder.UseSetting("Auth0:ClientSecret", "test-placeholder-not-used-for-this-check");
        builder.UseSetting("Auth0:Audience", "kinsmen-api");
        builder.UseSetting("RateLimit:PerMinute", "5");
        builder.UseSetting("RateLimit:WritesPerMinute", "2");
    }
}

public sealed class SecurityTests : IDisposable
{
    private readonly LimitedWebFactory factory = new();
    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task EveryResponseCarriesSecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self';", csp);          // no inline or third-party scripts
        Assert.Contains("frame-ancestors 'none'", csp);       // no clickjacking
        Assert.Contains("form-action 'self' https://kinsmen.eu.auth0.com", csp);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task StaticFilesAlsoCarrySecurityHeaders()
    {
        var response = await factory.CreateClient().GetAsync("/js/site.js");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task PagesAreRateLimitedPerVisitor()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Location")).StatusCode);
        var limited = await client.GetAsync("/Location");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task StaticFilesDoNotUseUpTheLimit()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 10; i++) await client.GetAsync("/css/kinsmen.css");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Location")).StatusCode);
    }

    [Fact]
    public async Task WritesHaveATighterLimitAppliedBeforeTheLoginCheck()
    {
        // Anonymous floods of a protected write are limited too, not just bounced to login forever.
        var client = factory.CreateClient();
        async Task<HttpResponseMessage> Post()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/Booking/Create") { Content = JsonContent("{}") };
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Post()).StatusCode);
        var limited = await Post();
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Too many requests", await limited.Content.ReadAsStringAsync());
    }

    [Fact]
    public void EveryPostActionHasARateLimitPolicy()
    {
        // Guards future changes: a new data-changing action must opt into the "writes" limit.
        var unprotected = typeof(HomeController).Assembly.GetTypes()
            .Where(t => typeof(Controller).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is not null)
            .Where(m => m.Name != nameof(AccountController.Logout)) // Logout is protected by its antiforgery token
            .Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName != RateLimitPolicies.Writes)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();
        Assert.Empty(unprotected);
    }

    [Fact]
    public void LoginAndRegisterUseTheLoginLimit()
    {
        foreach (var name in new[] { nameof(AccountController.Login), nameof(AccountController.Register) })
            Assert.Equal(RateLimitPolicies.Login,
                typeof(AccountController).GetMethod(name)!.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }

    private static StringContent JsonContent(string json) => new(json, System.Text.Encoding.UTF8, "application/json");
}

public sealed class ProxyHeadersHandlerTests
{
    private static async Task<HttpRequestMessage> Send(string? proxyKey, HttpContext? context)
    {
        var capture = new CaptureHandler();
        var accessor = new HttpContextAccessor { HttpContext = context };
        var handler = new ProxyHeadersHandler(accessor, Options.Create(new ApiClientOptions { ProxyKey = proxyKey })) { InnerHandler = capture };
        await new HttpMessageInvoker(handler).SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api/api/services"), default);
        return capture.Request!;
    }

    private static DefaultHttpContext Visitor(string ip)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }

    [Fact]
    public async Task IdentifiesTheVisitorWhenTheKeyIsConfigured()
    {
        var request = await Send("shared-proxy-key-0123456789abcdefghij", Visitor("203.0.113.7"));
        Assert.Equal("shared-proxy-key-0123456789abcdefghij", request.Headers.GetValues(ProxyHeadersHandler.ProxyKeyHeader).Single());
        Assert.Equal("ip:203.0.113.7", request.Headers.GetValues(ProxyHeadersHandler.ClientHeader).Single());
    }

    [Fact]
    public async Task SendsNothingWithoutAKey()
    {
        var request = await Send(null, Visitor("203.0.113.7"));
        Assert.False(request.Headers.Contains(ProxyHeadersHandler.ProxyKeyHeader));
        Assert.False(request.Headers.Contains(ProxyHeadersHandler.ClientHeader));
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
