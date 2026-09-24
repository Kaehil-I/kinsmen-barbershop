namespace Kinsmen.Web.Models.Api;

/// <summary>Booking status. JSON values are camelCase (pending, confirmed, cancelled,
/// completed, noShow) — see the JsonStringEnumConverter configured in KinsmenApiClient.</summary>
public enum BookingStatus
{
    Pending,
    Confirmed,
    Cancelled,
    Completed,
    NoShow
}

/// <summary>A service's price/duration/name as recorded on the booking at creation time.
/// This snapshot does not change even if the live Service catalogue changes later.</summary>
public sealed class ServiceSnapshot
{
    public string ServiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int DurationMinutes { get; set; }
}

/// <summary>A booking record, as returned by every booking endpoint.</summary>
public sealed class Booking
{
    public string Id { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>The assigned barber. Bookings are always assigned to one barber by the time
    /// they're returned, even if the create request used null ("any available barber").</summary>
    public string BarberId { get; set; } = string.Empty;

    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public List<ServiceSnapshot> Services { get; set; } = [];
    public int TotalCents { get; set; }
    public BookingStatus Status { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Resend the latest value of this on every reschedule/cancel/status change.
    /// A stale value returns 409 stale_version.</summary>
    public long Version { get; set; }

    /// <summary>Customer-supplied note for the barber, up to 500 chars. Render as text —
    /// never as raw HTML.</summary>
    public string? Notes { get; set; }
}
