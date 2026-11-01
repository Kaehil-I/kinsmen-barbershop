using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

public sealed class DashboardTests : IDisposable
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new(); // 2026-09-18 08:00 in Johannesburg
    private readonly ApiFactory factory = new();
    private ReportService Reports => new(store, clock);
    private static readonly Actor Admin = new("admin-a", "Admin");
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 30);

    public void Dispose() => factory.Dispose();

    private static ServiceSnapshot Haircut => new("haircut", "Demo haircut", 20000, 30);
    private static ServiceSnapshot Beard => new("beard", "Demo beard trim", 10000, 15);

    // Starts are given in shop time (UTC+2) so the tests read like the shop's day.
    private Booking Add(string id, string startLocal, BookingStatus status, int total = 20000, params ServiceSnapshot[] services)
    {
        var start = DateTimeOffset.Parse(startLocal + "+02:00").UtcDateTime;
        var booking = new Booking(id, "customer-a", "barber-a", start, start.AddMinutes(30),
            services.Length == 0 ? [Haircut] : services, total, status, start.AddDays(-2));
        store.Saved.Add(booking);
        return booking;
    }

    [Fact] public async Task OnlyAdminsCanSeeTheDashboard()
    {
        foreach (var actor in new[] { new Actor("c", "Customer"), new Actor("b", "Barber") })
            Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Reports.Dashboard(actor, From, To))).Status);
    }

    [Fact] public async Task EmptyPeriodReportsZeroes()
    {
        var report = await Reports.Dashboard(Admin, From, To);
        Assert.Equal(new DashboardTotals(0, 0, 0, 0, 0, 0), report.Totals);
        Assert.Empty(report.TopServices);
    }

    [Fact] public async Task TotalsCountEachOutcomeAndSumCompletedRevenue()
    {
        Add("done-1", "2026-09-02T10:00", BookingStatus.Completed, 30000, Haircut, Beard);
        Add("done-2", "2026-09-03T10:00", BookingStatus.Completed, 20000);
        Add("cancelled", "2026-09-04T10:00", BookingStatus.Cancelled);
        Add("noshow", "2026-09-05T10:00", BookingStatus.NoShow);
        Add("soon", "2026-09-20T10:00", BookingStatus.Confirmed);
        Add("later", "2026-09-25T10:00", BookingStatus.Pending);

        var totals = (await Reports.Dashboard(Admin, From, To)).Totals;

        Assert.Equal(6, totals.Total);
        Assert.Equal(2, totals.Completed);
        Assert.Equal(1, totals.Cancelled);
        Assert.Equal(1, totals.NoShow);
        Assert.Equal(2, totals.Upcoming);
        Assert.Equal(50000, totals.RevenueCents); // Completed only: cancelled and upcoming bookings earn nothing yet.
    }

    [Fact] public async Task UpcomingMeansNotYetStartedSoPastPendingBookingsAreNotCounted()
    {
        Add("stale", "2026-09-10T10:00", BookingStatus.Pending); // Before "now" (18 Sep) and never confirmed.
        Add("today-later", "2026-09-18T15:00", BookingStatus.Confirmed);
        Add("today-earlier", "2026-09-18T07:00", BookingStatus.Confirmed); // Started an hour ago.

        var totals = (await Reports.Dashboard(Admin, From, To)).Totals;

        Assert.Equal(1, totals.Upcoming);
        Assert.Equal(3, totals.Total);
    }

    [Fact] public async Task TheDateFilterUsesShopDaysAndIncludesBothEnds()
    {
        Add("midnight-start", "2026-09-01T00:00", BookingStatus.Completed); // First minute of the shop's 1 September.
        Add("last-evening", "2026-09-30T23:30", BookingStatus.Completed);   // Last half hour of 30 September.
        Add("before", "2026-08-31T23:30", BookingStatus.Completed);
        Add("after", "2026-10-01T00:00", BookingStatus.Completed);

        Assert.Equal(2, (await Reports.Dashboard(Admin, From, To)).Totals.Completed);
        Assert.Equal(1, (await Reports.Dashboard(Admin, From, From)).Totals.Completed); // One-day range.
    }

    [Fact] public async Task MostBookedServicesRankByBookingsAndIgnoreCancelled()
    {
        Add("a", "2026-09-02T10:00", BookingStatus.Completed, 30000, Haircut, Beard);
        Add("b", "2026-09-03T10:00", BookingStatus.Completed, 20000, Haircut);
        Add("c", "2026-09-04T10:00", BookingStatus.Confirmed, 10000, Beard);
        Add("d", "2026-09-05T10:00", BookingStatus.Cancelled, 10000, Beard);
        Add("e", "2026-09-06T10:00", BookingStatus.Cancelled, 10000, Beard);

        var top = (await Reports.Dashboard(Admin, From, To)).TopServices;

        // Two each. A tie is broken by name, and the two cancelled beard trims don't count (otherwise beard would lead with 4).
        Assert.Equal(["beard", "haircut"], top.Select(x => x.ServiceId));
        Assert.Equal([2, 2], top.Select(x => x.Bookings));
    }

    [Fact] public async Task LowStockListsActiveProductsAtOrBelowTheirOwnThresholdWorstFirst()
    {
        store.Inventory.Clear();
        store.Inventory.AddRange([
            new("plenty", "Plenty", 1000, 50, LowStockThreshold: 5),
            new("at-threshold", "At threshold", 1000, 5, LowStockThreshold: 5),
            new("gone", "Gone", 1000, 0, Category: "Styling", LowStockThreshold: 5),
            new("custom", "Custom threshold", 1000, 12, LowStockThreshold: 15),
            new("retired", "Retired", 1000, 0, Active: false)]);

        var low = (await Reports.Dashboard(Admin, From, To)).LowStock;

        Assert.Equal(["gone", "at-threshold", "custom"], low.Select(x => x.ProductId));
        Assert.Equal("Styling", low[0].Category);
    }

    [Theory]
    [InlineData("2026-09-30", "2026-09-01")]
    [InlineData("2025-01-01", "2026-09-30")]
    public async Task BackwardsOrOversizedPeriodsAreRejected(string from, string to)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Reports.Dashboard(Admin, DateOnly.Parse(from), DateOnly.Parse(to)))).Status);

    [Fact] public async Task EndpointReturnsTheReportToAdminsOnly()
    {
        var url = "/api/admin/dashboard?from=2026-09-01&to=2026-09-30";

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.Client().GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client("customer-a").GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await factory.Client("staff-a", "Barber").GetAsync(url)).StatusCode);

        var response = await factory.Client("admin-a", "Admin").GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2026-09-01", json.RootElement.GetProperty("from").GetString());
        Assert.True(json.RootElement.GetProperty("totals").TryGetProperty("revenueCents", out _));
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("lowStock").ValueKind);
    }

    [Fact] public async Task EndpointRejectsAnImpossiblePeriodWithABadRequest()
    {
        var response = await factory.Client("admin-a", "Admin").GetAsync("/api/admin/dashboard?from=2026-09-30&to=2026-09-01");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
