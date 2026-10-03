using Kinsmen.Web.Helpers;

namespace Kinsmen.Web.Tests;

public sealed class ShopTimeZoneProviderTests
{
    // barber-schedule.js builds block timestamps with a hardcoded "+02:00" offset, on
    // the basis that the shop's zone (South Africa) has no daylight saving. If this
    // ever stopped being true - or the zone failed to resolve and the fallback kicked
    // in with the wrong offset - times shown to customers would drift from the ones
    // the barber blocks off.
    [Theory]
    [InlineData(1)]  // January
    [InlineData(7)]  // July
    public void ShopTimeIsAlwaysTwoHoursAheadOfUtc(int month)
    {
        var utc = new DateTimeOffset(2026, month, 15, 8, 0, 0, TimeSpan.Zero);

        var local = TimeZoneInfo.ConvertTime(utc, ShopTimeZoneProvider.Instance);

        Assert.Equal(TimeSpan.FromHours(2), local.Offset);
        Assert.Equal(10, local.Hour);
    }
}
