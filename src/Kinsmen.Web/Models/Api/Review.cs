namespace Kinsmen.Web.Models.Api;

/// <summary>A review, as returned by POST /api/bookings/{id}/review.</summary>
public sealed class Review
{
    public string Id { get; set; } = string.Empty;
    public string BookingId { get; set; } = string.Empty;
    public string BarberId { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}