using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Everything the booking page needs on first load. The actual availability
/// and the booking submission itself happen via AJAX against BookingController's JSON
/// actions as the customer picks a barber/services/date — see wwwroot/js/booking.js.</summary>
public sealed class BookingPageViewModel
{
    public List<Service> Services { get; set; } = [];
    public List<Barber> Barbers { get; set; } = [];
    public bool ApiUnavailable { get; set; }
}
