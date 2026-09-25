namespace Kinsmen.Web.Models.Api;

/// <summary>Body for POST /api/bookings. Send the Idempotency-Key as a header, not here —
/// see IKinsmenApiClient.CreateBookingAsync.</summary>
public sealed class CreateBookingRequest
{
    /// <summary>Null selects any available barber.</summary>
    public string? BarberId { get; set; }

    public List<string> ServiceIds { get; set; } = [];

    public DateTimeOffset Start { get; set; }

    /// <summary>Admin-only: the customer this booking is for. Leave null for a customer
    /// booking their own appointment — the API takes their identity from the token.</summary>
    public string? CustomerId { get; set; }

    /// <summary>Up to 500 chars for the barber. Never render this back as HTML.</summary>
    public string? Notes { get; set; }
}

/// <summary>Body for PATCH /api/bookings/{id}/reschedule. Only the time changes —
/// barber and services stay as originally booked.</summary>
public sealed class RescheduleRequest
{
    public DateTimeOffset Start { get; set; }

    /// <summary>The booking's current version, from the last record you read.</summary>
    public long Version { get; set; }
}

/// <summary>Body for POST /api/bookings/{id}/cancel.</summary>
public sealed class VersionRequest
{
    public long Version { get; set; }
}

/// <summary>Body for PATCH /api/bookings/{id}/status. Status must be one of
/// Confirmed, Completed or NoShow — Cancelled goes through the dedicated cancel endpoint,
/// and Pending is a booking's initial state, never a target of this call.</summary>
public sealed class StatusChangeRequest
{
    public BookingStatus Status { get; set; }
    public long Version { get; set; }
}

/// <summary>Body for POST /api/barbers/{barberId}/blocks.</summary>
public sealed class CreateBlockRequest
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public string Reason { get; set; } = string.Empty;
}
