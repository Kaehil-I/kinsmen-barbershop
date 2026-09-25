using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Everything the barber schedule page needs on first load: the barber's own
/// bookings for the next 31 days (the API's own cap on one GET /api/bookings call,
/// same limitation as MyBookingsPageViewModel) and their time blocks for that same
/// window. MyBarberId is inferred from the first booking found, because the API has
/// no "who am I" endpoint that maps a staff token straight to a barberId — see the
/// comment in BarberScheduleController.Index for the full reasoning.</summary>
public sealed class BarberSchedulePageViewModel
{
    public List<Booking> Bookings { get; set; } = [];
    public List<TimeBlock> Blocks { get; set; } = [];
    public string? MyBarberId { get; set; }
    public string? ErrorMessage { get; set; }
}
