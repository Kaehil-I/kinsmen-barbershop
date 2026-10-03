using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Everything the My Bookings page needs on first load. Bookings only cover a
/// 31-day window — that's the API's own cap on a single GET /api/bookings call
/// (API-CONTRACT.md: "a positive interval no greater than 31 days"), and pagination
/// isn't implemented yet either, so a wider view isn't possible without multiple
/// stitched-together calls. Starts from today, so it's the next 31 days rather than
/// including history.</summary>
public sealed class MyBookingsPageViewModel
{
    public List<Booking> Bookings { get; set; } = [];
    public Dictionary<string, string> BarberNamesById { get; set; } = [];
    public string? ErrorMessage { get; set; }
}
