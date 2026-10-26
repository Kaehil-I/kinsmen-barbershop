namespace Kinsmen.Api.Domain;

/// <summary>Counts for the bookings that start inside the chosen dates. Upcoming means not yet started
/// (Pending or Confirmed); revenue is what completed appointments came to, products included.</summary>
public sealed record DashboardTotals(int Total, int Upcoming, int Completed, int Cancelled, int NoShow, long RevenueCents);
/// <summary>How many (non-cancelled) bookings in the period included a service.</summary>
public sealed record ServiceDemand(string ServiceId, string Name, int Bookings);
public sealed record LowStockItem(string ProductId, string Name, string? Category, int StockQuantity, int LowStockThreshold);
/// <summary>From and To are shop-local dates, both included. Low stock is current stock and ignores the dates.</summary>
public sealed record DashboardReport(DateOnly From, DateOnly To, DashboardTotals Totals, ServiceDemand[] TopServices, LowStockItem[] LowStock);

/// <summary>Read-only business summary for admins. Everything is worked out from the existing booking, service and
/// product records, so nothing is stored twice and the numbers can't drift from what the shop actually sees.</summary>
public sealed class ReportService(IBookingStore store, TimeProvider clock)
{
    public const int MaxRangeDays = 366;
    public const int TopServiceCount = 5;
    // Same explicit shop timezone as bookings: "today" and "this month" mean the shop's, not the server's.
    private static readonly TimeZoneInfo ShopZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");

    public Task<DashboardReport> Dashboard(Actor actor, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!actor.IsAdmin) throw DomainError.Forbidden();
        if (to < from) throw DomainError.Invalid("The end date can't be before the start date.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays) throw DomainError.Invalid($"Choose a period of at most {MaxRangeDays} days.");

        var fromUtc = StartOfDayUtc(from);
        var toUtc = StartOfDayUtc(to.AddDays(1));
        var now = clock.GetUtcNow().UtcDateTime;

        return store.Read(async s =>
        {
            // The store returns anything overlapping the window; only bookings that START in it are counted,
            // so an appointment spanning midnight isn't counted on two days.
            var bookings = (await s.Bookings(null, null, fromUtc, toUtc))
                .Where(b => b.StartUtc >= fromUtc && b.StartUtc < toUtc).ToList();

            var completed = bookings.Where(b => b.Status == BookingStatus.Completed).ToList();
            var totals = new DashboardTotals(
                Total: bookings.Count,
                Upcoming: bookings.Count(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed && b.StartUtc >= now),
                Completed: completed.Count,
                Cancelled: bookings.Count(b => b.Status == BookingStatus.Cancelled),
                NoShow: bookings.Count(b => b.Status == BookingStatus.NoShow),
                RevenueCents: completed.Sum(b => (long)b.TotalCents));

            // Named from the booking's own snapshot, so a renamed or deactivated service still reads as it was booked.
            var top = bookings.Where(b => b.Status != BookingStatus.Cancelled)
                .SelectMany(b => b.Services.DistinctBy(x => x.ServiceId))
                .GroupBy(x => x.ServiceId)
                .Select(g => new ServiceDemand(g.Key, g.Last().Name, g.Count()))
                .OrderByDescending(x => x.Bookings).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(TopServiceCount).ToArray();

            var lowStock = (await s.Products())
                .Where(p => p.Active && p.StockQuantity <= p.LowStockThreshold)
                .OrderBy(p => p.StockQuantity).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => new LowStockItem(p.Id, p.Name, p.Category, p.StockQuantity, p.LowStockThreshold)).ToArray();

            return new DashboardReport(from, to, totals, top, lowStock);
        }, ct);
    }

    private static DateTime StartOfDayUtc(DateOnly localDate)
        => TimeZoneInfo.ConvertTimeToUtc(localDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), ShopZone);
}
