using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.ApiClient;

/// <summary>Typed client for the Kinsmen booking API (src/Kinsmen.Api). Mirrors
/// docs/backend/API-CONTRACT.md one-to-one — see that document for role rules, booking
/// state transitions and the full error table. Every method throws KinsmenApiException
/// on a non-success response.</summary>
public interface IKinsmenApiClient
{
    // --- Public catalogue -------------------------------------------------

    Task<List<Service>> GetServicesAsync(CancellationToken cancellationToken = default);

    Task<List<Barber>> GetBarbersAsync(CancellationToken cancellationToken = default);

    /// <param name="date">The shop's local calendar date to check.</param>
    /// <param name="serviceIds">One or more services the customer wants in one visit.</param>
    /// <param name="barberId">Optional — omit to check every barber.</param>
    Task<List<AvailableSlot>> GetAvailabilityAsync(
        DateOnly date, IReadOnlyCollection<string> serviceIds, string? barberId = null,
        CancellationToken cancellationToken = default);

    // --- Bookings (require a token) ----------------------------------------

    /// <param name="idempotencyKey">Generate once per booking attempt (e.g. a GUID) and
    /// reuse the same key with the same request for retries — see API-CONTRACT.md
    /// "Safe retries". This client does not generate one for you, since the whole point
    /// is that the caller controls its lifetime across retry attempts.</param>
    Task<Booking> CreateBookingAsync(
        CreateBookingRequest request, string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <param name="from">Start of the window, inclusive.</param>
    /// <param name="to">End of the window, exclusive. Must be no more than 31 days after from.</param>
    /// <param name="barberId">Optional filter.</param>
    Task<List<Booking>> GetBookingsAsync(
        DateTimeOffset from, DateTimeOffset to, string? barberId = null,
        CancellationToken cancellationToken = default);

    Task<Booking> GetBookingAsync(string id, CancellationToken cancellationToken = default);

    Task<Booking> RescheduleBookingAsync(
        string id, RescheduleRequest request, CancellationToken cancellationToken = default);

    Task<Booking> CancelBookingAsync(
        string id, VersionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Barber/admin only. Status must be Confirmed, Completed or NoShow.</summary>
    Task<Booking> UpdateBookingStatusAsync(
        string id, StatusChangeRequest request, CancellationToken cancellationToken = default);

    // --- Barber time blocks (require a token) -------------------------------

    Task<List<TimeBlock>> GetBarberBlocksAsync(
        string barberId, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<TimeBlock> CreateBarberBlockAsync(
        string barberId, CreateBlockRequest request, CancellationToken cancellationToken = default);

    Task DeleteBarberBlockAsync(string blockId, CancellationToken cancellationToken = default);
}
