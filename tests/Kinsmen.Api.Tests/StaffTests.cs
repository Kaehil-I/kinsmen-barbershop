using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kinsmen.Api.Domain;
using Kinsmen.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kinsmen.Api.Tests;

// In-memory stand-ins: the tests never touch the real Auth0 tenant.
public sealed class FakeIdentityDirectory(bool configured = true) : IIdentityDirectory
{
    public Dictionary<string, IdentityUser> Users { get; } = new()
    {
        ["auth0|admin"] = new("auth0|admin", "owner@kinsmen.test", true, "Owner", "Admin"),
        ["auth0|other-admin"] = new("auth0|other-admin", "admin2@kinsmen.test", true, "Second admin", "Admin"),
        ["auth0|customer"] = new("auth0|customer", "sipho@kinsmen.test", true, "Sipho", "Customer"),
        ["auth0|unverified"] = new("auth0|unverified", "new@kinsmen.test", false, "New", "Customer"),
        ["staff-a"] = new("staff-a", "barber@kinsmen.test", true, "Barber A", "Barber")
    };
    public List<(string UserId, string Role)> RoleChanges { get; } = [];
    public bool IsConfigured => configured;
    public Task<List<IdentityUser>> FindByEmail(string email, CancellationToken ct)
        => Task.FromResult(Users.Values.Where(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)).ToList());
    public Task<IdentityUser?> FindById(string userId, CancellationToken ct) => Task.FromResult(Users.GetValueOrDefault(userId));
    public Task<List<IdentityUser>> ListStaff(CancellationToken ct) => Task.FromResult(Users.Values.Where(u => u.Role != "Customer").ToList());
    public Task SetRole(string userId, string role, CancellationToken ct)
    {
        RoleChanges.Add((userId, role));
        Users[userId] = Users[userId] with { Role = role };
        return Task.CompletedTask;
    }
}

public sealed class InMemoryAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];
    public Task Record(AuditEntry entry, CancellationToken ct) { Entries.Add(entry); return Task.CompletedTask; }
    public Task<List<AuditEntry>> Recent(int count, CancellationToken ct)
        => Task.FromResult(Entries.OrderByDescending(e => e.AtUtc).Take(count).ToList());
}

public sealed class StaffServiceTests
{
    private readonly FakeIdentityDirectory directory = new();
    private readonly InMemoryAuditLog audit = new();
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private StaffService Staff => new(directory, audit, store, clock, new());
    private static readonly Actor Admin = new("auth0|admin", "Admin");

    [Fact] public async Task OnlyAdminsCanManageStaff()
    {
        foreach (var actor in new Actor[] { new("auth0|customer", "Customer"), new("staff-a", "Barber") })
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Staff.ListStaff(actor))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(actor, "auth0|customer", "Admin"))).Status);
            Assert.Throws<DomainError>(() => Staff.IsEnabled(actor));
        }
    }

    [Fact] public async Task AdminCanPromoteAVerifiedCustomerAndTheChangeIsAudited()
    {
        var updated = await Staff.ChangeRole(Admin, "auth0|customer", "Admin");
        Assert.Equal("Admin", updated.Role);
        Assert.Equal([("auth0|customer", "Admin")], directory.RoleChanges);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(("auth0|admin", "auth0|customer", "sipho@kinsmen.test", "Customer", "Admin"),
            (entry.ActorId, entry.TargetUserId, entry.TargetEmail, entry.FromRole, entry.ToRole));
        Assert.Equal(clock.Now.UtcDateTime, entry.AtUtc);
    }

    [Fact] public async Task CustomersCanBeMadeBarbersAndBack()
    {
        await Staff.ChangeRole(Admin, "auth0|customer", "Barber");
        await Staff.ChangeRole(Admin, "auth0|customer", "Customer");
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact] public async Task YouCannotChangeYourOwnRole()
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(Admin, "auth0|admin", "Customer"));
        Assert.Equal("own_role", error.Code);
        Assert.Empty(directory.RoleChanges);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Barber")]
    public async Task AdminsCannotBeDemotedInTheApp(string role)
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(Admin, "auth0|other-admin", role));
        Assert.Equal("admin_demotion_not_allowed", error.Code);
        Assert.Empty(directory.RoleChanges);
        Assert.Empty(audit.Entries);
    }

    [Theory]
    [InlineData("Barber")]
    [InlineData("Admin")]
    public async Task UnverifiedEmailsCannotBeGivenStaffRoles(string role)
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(Admin, "auth0|unverified", role));
        Assert.Equal("email_not_verified", error.Code);
        Assert.Empty(directory.RoleChanges);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Owner")]
    [InlineData("admin")]
    public async Task OnlyKnownRolesAreAccepted(string? role)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(Admin, "auth0|customer", role))).Status);

    [Fact] public async Task UnknownPeopleReturnNotFound()
        => Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(Admin, "auth0|nobody", "Barber"))).Status);

    [Fact] public async Task SettingTheSameRoleIsANoOp()
    {
        Assert.Equal("Customer", (await Staff.ChangeRole(Admin, "auth0|customer", "Customer")).Role);
        Assert.Empty(directory.RoleChanges);
        Assert.Empty(audit.Entries);
    }

    [Fact] public async Task ABarberWithUpcomingBookingsCannotBeDemotedUntilTheyAreHandled()
    {
        // staff-a is linked to the demo profile barber-a.
        var booking = await new BookingService(store, clock, new()).Create(new("customer-x", "Customer"),
            new("barber-a", ["haircut"], BookingTests.Start));
        var error = await Assert.ThrowsAsync<DomainError>(() => Staff.ChangeRole(Admin, "staff-a", "Customer"));
        Assert.Equal("barber_has_bookings", error.Code);
        Assert.Empty(directory.RoleChanges);

        await new BookingService(store, clock, new()).Cancel(new("customer-x", "Customer"), booking.Id, booking.Version);
        await Staff.ChangeRole(Admin, "staff-a", "Customer");
        Assert.Equal("Customer", directory.Users["staff-a"].Role);
    }

    [Fact] public async Task PromotingABarberToAdminIsNotBlockedByTheirBookings()
    {
        await new BookingService(store, clock, new()).Create(new("customer-x", "Customer"), new("barber-a", ["haircut"], BookingTests.Start));
        await Staff.ChangeRole(Admin, "staff-a", "Admin");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("@kinsmen.test")]
    [InlineData("a@b@c")]
    public async Task LookupNeedsAnEmailAddress(string email)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Staff.FindByEmail(Admin, email))).Status);

    [Fact] public async Task LookupFindsAccountsByEmail()
        => Assert.Equal("auth0|customer", Assert.Single(await Staff.FindByEmail(Admin, "  SIPHO@kinsmen.test ")).UserId);

    [Fact] public async Task StaffListShowsAdminsFirst()
        => Assert.Equal(["Admin", "Admin", "Barber"], (await Staff.ListStaff(Admin)).Select(u => u.Role).ToArray());

    [Fact] public async Task EverythingButTheAuditLogIsOffUntilConfigured()
    {
        var off = new StaffService(new FakeIdentityDirectory(configured: false), audit, store, clock, new());
        Assert.False(off.IsEnabled(Admin));
        Assert.Equal("staff_management_disabled", (await Assert.ThrowsAsync<DomainError>(() => off.ListStaff(Admin))).Code);
        Assert.Equal(503, (await Assert.ThrowsAsync<DomainError>(() => off.ChangeRole(Admin, "auth0|customer", "Admin"))).Status);
        Assert.Empty(await off.RecentChanges(Admin));
    }
}

public sealed class Auth0ManagementDirectoryTests
{
    private sealed class FakeAuth0 : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Body, string? Auth)> Calls { get; } = [];
        public HttpStatusCode UsersStatus { get; set; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method, request.RequestUri!.PathAndQuery, body, request.Headers.Authorization?.ToString()));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/oauth/token") return Json(new { access_token = "mgmt-token", expires_in = 86400 });
            if (UsersStatus != HttpStatusCode.OK) return new HttpResponseMessage(UsersStatus) { Content = new StringContent("{\"message\":\"secret detail\"}") };
            if (path == "/api/v2/users-by-email" || path == "/api/v2/users")
                return Json(new[] { new { user_id = "auth0|1", email = "a@b.test", email_verified = true, name = "A", app_metadata = new { role = "Barber" } } });
            if (path.StartsWith("/api/v2/users/") && request.Method == HttpMethod.Get)
                return path.EndsWith("missing") ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : Json(new { user_id = "auth0|1", email = "a@b.test", email_verified = false, name = "A" });
            return Json(new { user_id = "auth0|1" });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }

    private static (Auth0ManagementDirectory, FakeAuth0) Create()
    {
        var fake = new FakeAuth0();
        var options = new Auth0ManagementOptions { Domain = "kinsmen.eu.auth0.com", ClientId = "id", ClientSecret = "secret" };
        var http = new HttpClient(fake) { BaseAddress = new Uri("https://kinsmen.eu.auth0.com/") };
        return (new Auth0ManagementDirectory(http, options, new Auth0TokenCache(), new TestClock(), NullLogger<Auth0ManagementDirectory>.Instance), fake);
    }

    [Fact] public async Task FetchesOneTokenAndReusesIt()
    {
        var (directory, fake) = Create();
        await directory.FindByEmail("a@b.test", default);
        await directory.ListStaff(default);
        var token = Assert.Single(fake.Calls, c => c.Path == "/oauth/token");
        Assert.Contains("\"audience\":\"https://kinsmen.eu.auth0.com/api/v2/\"", token.Body);
        Assert.Contains("\"grant_type\":\"client_credentials\"", token.Body);
        Assert.All(fake.Calls.Where(c => c.Path != "/oauth/token"), c => Assert.Equal("Bearer mgmt-token", c.Auth));
    }

    [Fact] public async Task MapsUsersAndDefaultsMissingRolesToCustomer()
    {
        var (directory, _) = Create();
        Assert.Equal(new IdentityUser("auth0|1", "a@b.test", true, "A", "Barber"), Assert.Single(await directory.FindByEmail("a@b.test", default)));
        Assert.Equal("Customer", (await directory.FindById("auth0|1", default))!.Role);
        Assert.Null(await directory.FindById("missing", default));
    }

    [Fact] public async Task SetRolePatchesOnlyTheRoleInAppMetadata()
    {
        var (directory, fake) = Create();
        await directory.SetRole("auth0|1", "Admin", default);
        var patch = Assert.Single(fake.Calls, c => c.Method == HttpMethod.Patch);
        Assert.Equal("/api/v2/users/auth0%7C1", patch.Path);
        Assert.Equal("{\"role\":\"Admin\"}", JsonDocument.Parse(patch.Body!).RootElement.GetProperty("app_metadata").GetRawText());
    }

    [Fact] public async Task EscapesTheEmailInLookups()
    {
        var (directory, fake) = Create();
        await directory.FindByEmail("a+b@c.test", default);
        Assert.Contains(fake.Calls, c => c.Path.StartsWith("/api/v2/users-by-email?email=a%2Bb%40c.test"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 503)]
    [InlineData(HttpStatusCode.Forbidden, 502)]
    public async Task Auth0FailuresBecomeSafeErrors(HttpStatusCode auth0Status, int expected)
    {
        var (directory, fake) = Create();
        fake.UsersStatus = auth0Status;
        var error = await Assert.ThrowsAsync<DomainError>(() => directory.ListStaff(default));
        Assert.Equal(expected, error.Status);
        Assert.DoesNotContain("secret detail", error.Message);
    }
}
