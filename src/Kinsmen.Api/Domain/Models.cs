using System.Text.Json.Serialization;

namespace Kinsmen.Api.Domain;

public sealed record ServiceItem(string Id, string Name, int PriceCents, int DurationMinutes, bool Active = true);
// Weekday uses ISO numbering: Monday=1 ... Sunday=7. Minutes are local shop time.
public sealed record WorkingPeriod(int Weekday, int StartMinute, int EndMinute);
public sealed record Barber(string Id, string Name, string UserId, WorkingPeriod[] Hours, bool Active = true, long Revision = 0);
public sealed record ServiceSnapshot(string ServiceId, string Name, int PriceCents, int DurationMinutes);
public enum BookingStatus { Pending, Confirmed, Cancelled, Completed, NoShow }
public sealed record Booking(string Id, string CustomerId, string BarberId, DateTime StartUtc, DateTime EndUtc,
    ServiceSnapshot[] Services, int TotalCents, BookingStatus Status, DateTime CreatedUtc, long Version = 1, string? Notes = null,
    [property: JsonIgnore] string? CreationFingerprint = null);
public sealed record TimeBlock(string Id, string BarberId, DateTime StartUtc, DateTime EndUtc, string Reason);
public sealed record Actor(string UserId, string Role)
{
    public bool IsAdmin => Role == "Admin";
    public bool IsBarber => Role == "Barber";
    public bool IsCustomer => Role == "Customer";
}
public sealed record CreateBookingRequest(string? BarberId, string[] ServiceIds, DateTimeOffset Start, string? CustomerId = null, string? Notes = null);
public sealed record RescheduleRequest(DateTimeOffset Start, long Version);
public sealed record VersionRequest(long Version);
public sealed record StatusRequest(BookingStatus Status, long Version);
public sealed record BlockRequest(DateTimeOffset Start, DateTimeOffset End, string Reason);
public sealed record AvailableSlot(string BarberId, DateTime StartUtc, DateTime EndUtc);
// Admin catalogue management. Records are deactivated, never deleted, so booking history stays intact.
public sealed record ServiceRequest(string Name, int PriceCents, int DurationMinutes);
public sealed record BarberRequest(string Name, string UserId, WorkingPeriod[] Hours);
public sealed record ActiveRequest(bool Active);
// Admin view of a barber: includes the linked identity and active flag, but not the internal revision counter.
public sealed record BarberProfile(string Id, string Name, string UserId, WorkingPeriod[] Hours, bool Active);
public sealed record BookingPolicy(int MinimumNoticeMinutes = 60, int CancellationNoticeMinutes = 60,
    int HorizonDays = 30, int SlotMinutes = 15);

public sealed class DomainError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public static DomainError Invalid(string message) => new(400, "invalid_request", message);
    public static DomainError Missing() => new(404, "not_found", "The requested record was not found.");
    public static DomainError Forbidden() => new(403, "forbidden", "This action is not permitted for this account.");
    public static DomainError Conflict(string message) => new(409, "booking_conflict", message);
}

// Repository signal handled by Create when concurrent idempotent requests race.
public sealed class DuplicateBookingIdException : Exception;
// Repository signal for the unique barbers.userId index: one barber profile per staff identity.
public sealed class DuplicateBarberUserException : Exception;
