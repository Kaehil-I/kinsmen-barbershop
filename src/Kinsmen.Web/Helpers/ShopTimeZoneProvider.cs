namespace Kinsmen.Web.Helpers;

/// <summary>Resolves the shop's timezone once, shared by every controller that needs to
/// convert the API's UTC timestamps to shop-local time for display (BookingController,
/// MyBookingsController).</summary>
public static class ShopTimeZoneProvider
{
    // Africa/Johannesburg has no DST, so this offset is always correct — but resolve
    // it properly rather than hardcoding, in case that ever changes.
    public static readonly TimeZoneInfo Instance = Resolve();

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("SAST", TimeSpan.FromHours(2), "SAST", "SAST");
        }
    }
}
