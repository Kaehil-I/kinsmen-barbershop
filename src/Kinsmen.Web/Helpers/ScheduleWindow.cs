namespace Kinsmen.Web.Helpers;

/// <summary>The date ranges the barber schedule page asks the API for.</summary>
public static class ScheduleWindow
{
    /// <summary>Days of history kept on the schedule. The API only lets staff mark a
    /// booking Completed once it has finished, and it lists bookings by overlap with the
    /// requested range - so a range that starts "now" makes an appointment drop off the
    /// page at the exact moment it becomes completable. Starting from the beginning of
    /// today (plus a few days back, for anything left unresolved) fixes that.</summary>
    public const int LookbackDays = 3;

    /// <summary>One list call is capped at 31 days by the API (API-CONTRACT.md).</summary>
    public const int SpanDays = 31;

    public static (DateTimeOffset From, DateTimeOffset To) ForBookings(DateTimeOffset nowUtc)
    {
        var from = StartOfShopDayUtc(nowUtc).AddDays(-LookbackDays);
        return (from, from.AddDays(SpanDays));
    }

    public static (DateTimeOffset From, DateTimeOffset To) ForBlocks(DateTimeOffset nowUtc)
    {
        var from = StartOfShopDayUtc(nowUtc);
        return (from, from.AddDays(SpanDays));
    }

    /// <summary>Midnight at the start of the shop's current day, as a UTC instant. This is
    /// local midnight, not UTC midnight - for a shop two hours ahead of UTC they differ,
    /// and a barber looking at the schedule at 01:30 is already on tomorrow's date.</summary>
    public static DateTimeOffset StartOfShopDayUtc(DateTimeOffset nowUtc)
    {
        var shopNow = TimeZoneInfo.ConvertTime(nowUtc, ShopTimeZoneProvider.Instance);
        var utc = TimeZoneInfo.ConvertTimeToUtc(shopNow.Date, ShopTimeZoneProvider.Instance);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
