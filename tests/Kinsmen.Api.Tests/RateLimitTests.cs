using System.Net;
using System.Net.Http.Json;
using Kinsmen.Api.Infrastructure;

namespace Kinsmen.Api.Tests;

public sealed class RateLimitTests : IDisposable
{
    private const string ProxyKey = "test-proxy-key-0123456789abcdefghijklmnop";
    private readonly ApiFactory factory = new(new Dictionary<string, string>
    {
        ["RateLimit:PermitPerMinute"] = "3",
        ["Proxy:Key"] = ProxyKey
    });
    public void Dispose() => factory.Dispose();

    private static async Task<HttpStatusCode[]> Hit(HttpClient client, int times, string path = "/api/services")
    {
        var codes = new HttpStatusCode[times];
        for (var i = 0; i < times; i++) codes[i] = (await client.GetAsync(path)).StatusCode;
        return codes;
    }

    private HttpClient ViaWebApp(string visitor, string key = ProxyKey)
    {
        var client = factory.Client();
        client.DefaultRequestHeaders.Add(RateLimitKeys.ProxyKeyHeader, key);
        client.DefaultRequestHeaders.Add(RateLimitKeys.ClientHeader, visitor);
        return client;
    }

    [Fact] public async Task ExceedingTheLimitReturns429WithRetryAfter()
    {
        var client = factory.Client();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK], await Hit(client, 3));
        var limited = await client.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("60", limited.Headers.GetValues("Retry-After").Single());
    }

    [Fact] public async Task SignedInUsersHaveSeparateLimits()
    {
        var first = factory.Client("customer-a");
        await Hit(first, 3);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await first.GetAsync("/api/services")).StatusCode);
        // Same address, different account: unaffected.
        Assert.Equal(HttpStatusCode.OK, (await factory.Client("customer-b").GetAsync("/api/services")).StatusCode);
    }

    [Fact] public async Task VisitorsVouchedForByTheWebAppHaveSeparateLimits()
    {
        await Hit(ViaWebApp("ip:203.0.113.1"), 3);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await ViaWebApp("ip:203.0.113.1").GetAsync("/api/services")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ViaWebApp("ip:203.0.113.2").GetAsync("/api/services")).StatusCode);
    }

    [Fact] public async Task CallersCannotPickTheirOwnBucketWithoutTheProxyKey()
    {
        // A wrong key means the visitor header is ignored, so both "visitors" share the caller's IP bucket.
        await Hit(ViaWebApp("ip:198.51.100.1", key: "wrong-key-wrong-key-wrong-key-wrong-key"), 3);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await ViaWebApp("ip:198.51.100.2", key: "wrong-key-wrong-key-wrong-key-wrong-key").GetAsync("/api/services")).StatusCode);
    }

    [Fact] public async Task HealthChecksAreNeverLimited()
    {
        var client = factory.Client();
        await Hit(client, 3);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/services")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact] public void ShortProxyKeyIsRejectedAtStartup()
    {
        using var badFactory = new ApiFactory(new Dictionary<string, string> { ["Proxy:Key"] = "too-short" });
        var error = Assert.ThrowsAny<Exception>(() => badFactory.CreateClient());
        Assert.Contains("Proxy:Key", error.ToString());
    }

    [Fact] public async Task BookingLimitErrorReachesTheClientAsAProblem()
    {
        using var api = new ApiFactory();
        var client = api.Client("customer-a");
        for (var hour = 0; hour < 3; hour++)
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/bookings",
                new { barberId = "barber-a", serviceIds = new[] { "haircut" }, start = BookingTests.Start.AddHours(hour) })).StatusCode);
        var fourth = await client.PostAsJsonAsync("/api/bookings",
            new { barberId = "barber-a", serviceIds = new[] { "haircut" }, start = BookingTests.Start.AddHours(3) });
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        Assert.Contains("booking_limit", await fourth.Content.ReadAsStringAsync());
    }
}
