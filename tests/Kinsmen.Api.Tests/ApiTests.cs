using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Kinsmen.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace Kinsmen.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Only the in-process test host uses this known key. It is never production configuration.
    public const string TestKey = "in-process-test-only-key-not-for-deployment-12345";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Auth:DevelopmentSigningKey", TestKey);
        builder.UseSetting("Auth:Issuer", "kinsmen-local"); builder.UseSetting("Auth:Audience", "kinsmen-api");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBookingStore>(); services.AddSingleton<IBookingStore, TestStore>();
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider, TestClock>();
        });
    }
    public HttpClient Client(string? user = null, string role = "Customer", bool expired = false, string audience = "kinsmen-api", bool includeSubject = true)
    {
        var client = CreateClient();
        if (user is null) return client;
        var claims = new List<Claim> { new("role", role) };
        if (includeSubject) claims.Add(new("sub", user));
        var token = new JwtSecurityToken("kinsmen-local", audience, claims,
            expires: DateTime.UtcNow.AddMinutes(expired ? -30 : 30),
            signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
}

public sealed class ApiTests : IDisposable
{
    private readonly ApiFactory factory = new();
    public void Dispose() => factory.Dispose();
    private static object Request => new { barberId = "barber-a", serviceIds = new[] { "haircut", "beard" }, start = "2026-09-19T10:00:00+02:00" };
    [Fact] public async Task PublicCatalogueDoesNotExposeStaffIdentity()
    {
        var client = factory.Client(); var response = await client.GetAsync("/api/barbers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("staff-a", await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task AnonymousBookingIsDenied()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().PostAsJsonAsync("/api/bookings", Request)).StatusCode);
    [Fact] public async Task ExpiredTokenIsDenied()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client("a", expired: true).PostAsJsonAsync("/api/bookings", Request)).StatusCode);
    [Fact] public async Task WrongAudienceIsDenied()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client("a", audience: "another-api").PostAsJsonAsync("/api/bookings", Request)).StatusCode);
    [Fact] public async Task MissingSubjectIsDenied()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client("a", includeSubject: false).PostAsJsonAsync("/api/bookings", Request)).StatusCode);
    [Fact] public async Task UnknownRoleIsDenied()
        => Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client("a", "Owner").PostAsJsonAsync("/api/bookings", Request)).StatusCode);
    [Fact] public async Task ApiCreatesBookingAndReturnsConflictOnOverlap()
    {
        var client = factory.Client("customer-a");
        var created = await client.PostAsJsonAsync("/api/bookings", Request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains("\"totalCents\":30000", await created.Content.ReadAsStringAsync());
        var conflict = await client.PostAsJsonAsync("/api/bookings", Request);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType!.MediaType);
    }
    [Fact] public async Task ApiRejectsClientPriceTampering()
    {
        var result = await factory.Client("a").PostAsJsonAsync("/api/bookings", new
            { barberId = "barber-a", serviceIds = new[] { "haircut" }, start = "2026-09-19T10:00:00+02:00", totalCents = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
    [Fact] public async Task InvalidBodyAndMissingServicesReturnBadRequest()
    {
        var client = factory.Client("a");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/bookings", new { barberId = "barber-a", start = "2026-09-19T10:00:00+02:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/bookings", new StringContent("{bad", Encoding.UTF8, "application/json"))).StatusCode);
    }
    [Fact] public async Task OtherCustomerCannotCancelThroughApi()
    {
        var first = await factory.Client("a").PostAsJsonAsync("/api/bookings", Request);
        var body = await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = body.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client("b").PostAsJsonAsync($"/api/bookings/{id}/cancel", new { version = 1 })).StatusCode);
    }
    [Fact] public async Task AvailabilityAcceptsRepeatedServiceQueryParameters()
    {
        var response = await factory.Client().GetAsync("/api/availability?date=2026-09-19&serviceIds=haircut&serviceIds=beard&barberId=barber-a");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("2026-09-19T07:00:00Z", await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task TimestampWithoutOffsetIsRejected()
    {
        var result = await factory.Client("a").PostAsJsonAsync("/api/bookings", new
            { barberId = "barber-a", serviceIds = new[] { "haircut" }, start = "2026-09-19T10:00:00" });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
    [Fact] public async Task BookingApiReturnsPendingThenStaffCanConfirm()
    {
        var created = await factory.Client("customer-a").PostAsJsonAsync("/api/bookings", Request);
        var body = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("pending", body.GetProperty("status").GetString());
        var id = body.GetProperty("id").GetString();
        var response = await factory.Client("staff-a", "Barber").PatchAsJsonAsync($"/api/bookings/{id}/status", new { status = "confirmed", version = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"confirmed\"", await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task IdempotencyHeaderPreventsDuplicatesAndInternalFingerprintIsPrivate()
    {
        var client = factory.Client("customer-a");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "api-request-key-001");
        var first = await client.PostAsJsonAsync("/api/bookings", Request);
        var second = await client.PostAsJsonAsync("/api/bookings", Request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.DoesNotContain("creationFingerprint", await first.Content.ReadAsStringAsync());
        var body = await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = body.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/bookings/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await factory.Client("other-customer").GetAsync($"/api/bookings/{id}")).StatusCode);
    }
    [Fact] public async Task InvalidIdempotencyHeaderReturnsBadRequest()
    {
        var client = factory.Client("customer-a"); client.DefaultRequestHeaders.Add("Idempotency-Key", "short");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/bookings", Request)).StatusCode);
    }
}
