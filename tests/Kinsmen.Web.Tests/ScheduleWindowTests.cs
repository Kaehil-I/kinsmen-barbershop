using Kinsmen.Web.Helpers;

namespace Kinsmen.Web.Tests;

public sealed class ScheduleWindowTests
{
    // 10:00 UTC on 28 September is midday in Durban (UTC+2), so the shop's day started at
    // 22:00 UTC the evening before - not at UTC midnight.
    [Fact]
    public void ShopDayStartsAtLocalMidnightNotUtcMidnight()
    {
        var start = ScheduleWindow.StartOfShopDayUtc(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero), start);
    }

    // The classic timezone slip: at 23:30 UTC it's already 01:30 tomorrow in the shop, so
    // "today" for a barber looking at the page is the 29th, not the 28th.
    [Fact]
    public void LateEveningUtcIsAlreadyTomorrowInTheShop()
    {
        var start = ScheduleWindow.StartOfShopDayUtc(new DateTimeOffset(2026, 9, 28, 23, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero), start);
    }

    // The API only lets staff mark a booking Completed once it has ended, and it lists by
    // overlap with the requested range. A range starting at "now" would drop an appointment
    // the moment it becomes completable, so the window has to reach back past now.
    [Fact]
    public void BookingWindowReachesBackSoFinishedAppointmentsCanStillBeCompleted()
    {
        var now = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

        var (from, _) = ScheduleWindow.ForBookings(now);

        Assert.True(from < now.AddDays(-2), "The window should start a few days before today.");
        // Concretely: local midnight of the 25th (three shop-days before the 28th).
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 22, 0, 0, TimeSpan.Zero), from);
    }

    [Fact]
    public void BookingWindowStaysWithinTheApisThirtyOneDayCap()
    {
        var (from, to) = ScheduleWindow.ForBookings(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

        Assert.True(to > from);
        Assert.True(to - from <= TimeSpan.FromDays(31));
    }

    [Fact]
    public void BlockWindowStartsAtTheBeginningOfTodayAndStaysWithinTheCap()
    {
        var (from, to) = ScheduleWindow.ForBlocks(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero), from);
        Assert.True(to - from <= TimeSpan.FromDays(31));
    }
}
