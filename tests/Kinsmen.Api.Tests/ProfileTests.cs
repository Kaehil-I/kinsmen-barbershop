using System.Net;
using System.Net.Http.Json;
using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

public sealed class InMemoryProfileStore : IProfileStore
{
    public Dictionary<string, CustomerProfile> Profiles { get; } = [];
    public Task<CustomerProfile?> Get(string userId, CancellationToken ct)
        => Task.FromResult(Profiles.TryGetValue(userId, out var p) ? p : null);
    public Task Save(CustomerProfile profile, CancellationToken ct) { Profiles[profile.Id] = profile; return Task.CompletedTask; }
}

public sealed class ProfileServiceTests
{
    private readonly InMemoryProfileStore profiles = new();
    private readonly TestStore store = new();
    private ProfileService Service => new(profiles, store, new TestClock());
    private static readonly Actor Customer = new("customer-1", "Customer");

    [Theory]
    [InlineData("0821234567", "+27821234567")]
    [InlineData("082 123 4567", "+27821234567")]
    [InlineData("+27 82 123 4567", "+27821234567")]
    [InlineData("27821234567", "+27821234567")]
    [InlineData("(031) 555-0101", "+27315550101")]
    public void SouthAfricanNumbersAreStoredInOneFormat(string input, string expected)
        => Assert.Equal(expected, ProfileService.NormalisePhone(input));

    [Theory]
    [InlineData("12345")]
    [InlineData("08212345678")]
    [InlineData("+44 20 7946 0958")]
    [InlineData("0021234567")]
    public void OtherNumbersAreRejected(string input)
        => Assert.Equal(400, Assert.Throws<DomainError>(() => ProfileService.NormalisePhone(input)).Status);

    [Fact] public async Task NewCustomerGetsAnEmptyProfile()
    {
        var profile = await Service.Get(Customer, default);
        Assert.Equal("customer-1", profile.Id);
        Assert.Null(profile.DisplayName); Assert.Null(profile.Phone); Assert.Null(profile.PreferredBarberId);
    }

    [Fact] public async Task SavedDetailsAreTrimmedAndNormalised()
    {
        var saved = await Service.Save(Customer, new("  Thabo  ", "082 123 4567", "barber-a"), default);
        Assert.Equal("Thabo", saved.DisplayName);
        Assert.Equal("+27821234567", saved.Phone);
        Assert.Equal("barber-a", saved.PreferredBarberId);
        Assert.Equal(saved, profiles.Profiles["customer-1"]);
    }

    [Fact] public async Task BlankFieldsClearThePreviousValue()
    {
        await Service.Save(Customer, new("Thabo", "0821234567", "barber-a"), default);
        var cleared = await Service.Save(Customer, new("", " ", null), default);
        Assert.Null(cleared.DisplayName); Assert.Null(cleared.Phone); Assert.Null(cleared.PreferredBarberId);
    }

    [Fact] public async Task TooLongNameIsRejected()
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Service.Save(Customer, new(new string('a', 61), null, null), default));
        Assert.Equal(400, error.Status);
    }

    [Fact] public async Task UnknownOrInactiveBarberIsRejected()
    {
        await Assert.ThrowsAsync<DomainError>(() => Service.Save(Customer, new(null, null, "no-such-barber"), default));
        store.Staff[0] = store.Staff[0] with { Active = false };
        await Assert.ThrowsAsync<DomainError>(() => Service.Save(Customer, new(null, null, store.Staff[0].Id), default));
        Assert.Empty(profiles.Profiles);
    }

    [Theory]
    [InlineData("Barber")]
    [InlineData("Admin")]
    public async Task StaffHaveNoCustomerProfile(string role)
    {
        var staff = new Actor("staff-a", role);
        Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Service.Get(staff, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Service.Save(staff, new("x", null, null), default))).Status);
    }
}

public sealed class ProfileApiTests : IDisposable
{
    private readonly ApiFactory factory = new();
    public void Dispose() => factory.Dispose();

    [Fact] public async Task AnonymousCallersAreDenied()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().GetAsync("/api/me/profile")).StatusCode);

    [Fact] public async Task CustomersOnlySeeTheirOwnProfile()
    {
        var alice = factory.Client("alice");
        var put = await alice.PutAsJsonAsync("/api/me/profile", new { displayName = "Alice", phone = "0821234567", preferredBarberId = "barber-a" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var mine = await alice.GetFromJsonAsync<CustomerProfile>("/api/me/profile");
        Assert.Equal("Alice", mine!.DisplayName);
        Assert.Equal("+27821234567", mine.Phone);

        var bobs = await factory.Client("bob").GetFromJsonAsync<CustomerProfile>("/api/me/profile");
        Assert.Equal("bob", bobs!.Id);
        Assert.Null(bobs.DisplayName);
    }

    [Fact] public async Task InvalidPhoneReturnsAProblem()
    {
        var response = await factory.Client("alice").PutAsJsonAsync("/api/me/profile", new { phone = "12345" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("South African phone number", await response.Content.ReadAsStringAsync());
    }

    [Fact] public async Task BarbersCannotUseTheCustomerProfile()
        => Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client("staff-a", "Barber").GetAsync("/api/me/profile")).StatusCode);
}
