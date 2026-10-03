namespace Kinsmen.Web.Models.Api;

/// <summary>One open barber/start/end combination from GET /api/availability.
/// Advisory only — the create-booking transaction is the authoritative check.</summary>
public sealed class AvailableSlot
{
    public string BarberId { get; set; } = string.Empty;
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
}
