using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Controllers;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kinsmen.Web.Tests;

public sealed class DashboardPeriodTests
{
    // Wednesday 14 October 2026.
    private static readonly DateOnly Today = new(2026, 10, 14);

    [Fact]
    public void NoFilterMeansThisMonth()
    {
        var period = DashboardPeriod.Resolve(null, null, null, Today);

        Assert.Equal(DashboardPeriod.Month, period.Preset);
        Assert.Equal(new DateOnly(2026, 10, 1), period.From);
        Assert.Equal(new DateOnly(2026, 10, 31), period.To);
        Assert.Null(period.Problem);
    }

    [Fact]
    public void ThisWeekRunsMondayToSunday()
    {
        var period = DashboardPeriod.Resolve(DashboardPeriod.Week, null, null, Today);

        Assert.Equal(new DateOnly(2026, 10, 12), period.From);
        Assert.Equal(new DateOnly(2026, 10, 18), period.To);
    }

    [Fact]
    public void ThisWeekFromASundayStillStartsOnTheMondayBefore()
    {
        var period = DashboardPeriod.Resolve(DashboardPeriod.Week, null, null, new DateOnly(2026, 10, 18));

        Assert.Equal(new DateOnly(2026, 10, 12), period.From);
        Assert.Equal(new DateOnly(2026, 10, 18), period.To);
    }

    [Fact]
    public void RollingPresetsCountTodayAsOneOfThirtyDays()
    {
        var last = DashboardPeriod.Resolve(DashboardPeriod.Last30, null, null, Today);
        var next = DashboardPeriod.Resolve(DashboardPeriod.Next30, null, null, Today);

        Assert.Equal((new DateOnly(2026, 9, 15), Today), (last.From, last.To));
        Assert.Equal((Today, new DateOnly(2026, 11, 12)), (next.From, next.To));
    }

    [Fact]
    public void CustomDatesAreUsedAsGiven()
    {
        var period = DashboardPeriod.Resolve(DashboardPeriod.Custom, new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9), Today);

        Assert.Equal(DashboardPeriod.Custom, period.Preset);
        Assert.Equal((new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 9)), (period.From, period.To));
        Assert.Null(period.Problem);
    }

    [Fact]
    public void ASingleDayIsAValidCustomPeriod()
    {
        var period = DashboardPeriod.Resolve(DashboardPeriod.Custom, Today, Today, Today);

        Assert.Null(period.Problem);
        Assert.Equal((Today, Today), (period.From, period.To));
    }

    [Theory]
    [InlineData("2026-10-10", null)]
    [InlineData(null, "2026-10-10")]
    [InlineData("2026-10-10", "2026-10-01")]
    [InlineData("2024-01-01", "2026-10-01")]
    public void UnusableCustomPeriodsFallBackToThisMonthAndSayWhy(string? from, string? to)
    {
        var period = DashboardPeriod.Resolve(DashboardPeriod.Custom,
            from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), Today);

        Assert.Equal(DashboardPeriod.Month, period.Preset);
        Assert.Equal(new DateOnly(2026, 10, 1), period.From);
        Assert.False(string.IsNullOrWhiteSpace(period.Problem));
    }

    [Fact]
    public void AnUnknownPresetIsTreatedAsNoFilterWithoutAComplaint()
    {
        var period = DashboardPeriod.Resolve("nonsense", null, null, Today);

        Assert.Equal(DashboardPeriod.Month, period.Preset);
        Assert.Null(period.Problem);
    }
}

public sealed class DashboardControllerTests
{
    private sealed class FakeDashboardApi : IDashboardApiClient
    {
        public Exception? Failure { get; set; }
        public (DateOnly From, DateOnly To)? Requested { get; private set; }
        public Task<DashboardReport> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requested = (from, to);
            if (Failure is not null) throw Failure;
            return Task.FromResult(new DashboardReport { From = from, To = to });
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // 22:30 UTC on 31 October is already 1 November in the shop (UTC+2).
    private static DashboardController Create(FakeDashboardApi api, string utcNow = "2026-10-31T22:30:00Z")
        => new(api, new FixedClock(DateTimeOffset.Parse(utcNow)));

    [Fact]
    public async Task OnlyAdminsCanOpenTheDashboard()
    {
        var authorize = typeof(DashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single();

        Assert.Equal("Admin", authorize.Roles);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TodayAndTheMonthAreWorkedOutInShopTimeNotUtc()
    {
        var api = new FakeDashboardApi();

        await Create(api).Index(null, null, null, default);

        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), api.Requested);
    }

    [Fact]
    public async Task TheChosenPeriodIsWhatTheApiIsAsked()
    {
        var api = new FakeDashboardApi();

        var model = (DashboardPageViewModel)((ViewResult)await Create(api).Index(
            DashboardPeriod.Custom, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), default)).Model!;

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7)), api.Requested);
        Assert.NotNull(model.Report);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task ABadPeriodDoesNotReachTheApiAsEntered()
    {
        var api = new FakeDashboardApi();

        var model = (DashboardPageViewModel)((ViewResult)await Create(api).Index(
            DashboardPeriod.Custom, new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 1), default)).Model!;

        Assert.Equal(DashboardPeriod.Month, model.Period.Preset);
        Assert.NotNull(model.Period.Problem);
        Assert.Equal(model.Period.From, api.Requested!.Value.From);
    }

    [Fact]
    public async Task AnApiFailureShowsAMessageInsteadOfCrashing()
    {
        var api = new FakeDashboardApi { Failure = new KinsmenApiException(403, null, "raw") };

        var model = (DashboardPageViewModel)((ViewResult)await Create(api).Index(null, null, null, default)).Model!;

        Assert.Null(model.Report);
        Assert.Contains("permission", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnUnreachableApiShowsTheStartingUpMessage()
    {
        var api = new FakeDashboardApi { Failure = new HttpRequestException("down") };

        var model = (DashboardPageViewModel)((ViewResult)await Create(api).Index(null, null, null, default)).Model!;

        Assert.True(ApiErrorMessages.IsWakingUp(model.ErrorMessage));
    }
}

/// <summary>The real page, rendered through the whole web app with a stand-in sign-in and a fake API.</summary>
public sealed class DashboardPageTests : IClassFixture<WebFactory>
{
    private const string RoleHeader = "X-Test-Role";

    private sealed class HeaderAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "auth0|tester"), new Claim(ClaimTypes.Role, role.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private sealed class CannedApi(DashboardReport report) : IDashboardApiClient
    {
        public DateOnly? AskedFrom { get; private set; }
        public Task<DashboardReport> GetAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            AskedFrom = from;
            return Task.FromResult(report);
        }
    }

    private static DashboardReport Report => new()
    {
        From = new DateOnly(2026, 10, 1),
        To = new DateOnly(2026, 10, 31),
        Totals = new() { Total = 20, Upcoming = 6, Completed = 9, Cancelled = 4, NoShow = 1, RevenueCents = 245000 },
        TopServices = [new() { ServiceId = "cut", Name = "Skin fade", Bookings = 12 }, new() { ServiceId = "beard", Name = "Beard trim", Bookings = 1 }],
        LowStock = [new() { ProductId = "p1", Name = "Matify", Category = "Styling", StockQuantity = 0, LowStockThreshold = 5 },
                    new() { ProductId = "p2", Name = "Beard Oil", StockQuantity = 3, LowStockThreshold = 5 }]
    };

    private readonly WebFactory factory;
    public DashboardPageTests(WebFactory factory) => this.factory = factory;

    private HttpClient Client(DashboardReport? report = null, string? role = null)
    {
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, HeaderAuth>("Test", _ => { });
            services.Configure<AuthenticationOptions>(o => o.DefaultScheme = o.DefaultAuthenticateScheme = o.DefaultChallengeScheme = "Test");
            services.RemoveAll<IDashboardApiClient>();
            services.AddSingleton<IDashboardApiClient>(new CannedApi(report ?? Report));
        })).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (role is not null) client.DefaultRequestHeaders.Add(RoleHeader, role);
        return client;
    }

    [Fact]
    public async Task AdminsSeeTheTotalsTopServicesAndLowStock()
    {
        var response = await Client(role: "Admin").GetAsync("/Dashboard");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Skin fade", html);
        Assert.Contains("12 bookings", html);
        Assert.Contains("1 booking<", html);              // Singular.
        Assert.Contains("R2450 earned", html);             // Completed revenue.
        Assert.Contains("20% of all bookings", html);      // 4 of 20 cancelled.
        Assert.Contains("Out of stock", html);              // Matify.
        Assert.Contains("3 left", html);                    // Beard Oil.
        Assert.Contains("aria-current=\"true\"", html);     // The chosen period is marked for screen readers.
        Assert.Contains("This month", html);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("Barber")]
    public async Task OtherSignedInRolesAreKeptOut(string role)
    {
        var response = await Client(role: role).GetAsync("/Dashboard");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousVisitorsAreKeptOut()
    {
        var response = await Client().GetAsync("/Dashboard");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AQuietPeriodSaysSoAndNoLowStockSaysThatToo()
    {
        var quiet = new DashboardReport { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31) };

        var html = await (await Client(quiet, "Admin").GetAsync("/Dashboard")).Content.ReadAsStringAsync();

        Assert.Contains("No bookings in this period yet.", html);
        Assert.Contains("All products are above their low-stock levels.", html);
        Assert.DoesNotContain("Low stock", html);
    }

    [Fact]
    public async Task OnlyAdminsGetTheDashboardLinkInTheMenu()
    {
        var admin = await (await Client(role: "Admin").GetAsync("/Dashboard")).Content.ReadAsStringAsync();
        var customer = await (await Client(role: "Customer").GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("href=\"/Dashboard\"", admin);
        Assert.DoesNotContain("href=\"/Dashboard\"", customer);
    }
}
