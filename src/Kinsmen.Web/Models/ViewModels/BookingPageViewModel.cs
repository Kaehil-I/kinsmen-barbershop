using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Everything the booking page needs on first load. The actual availability
/// and the booking submission itself happen via AJAX against BookingController's JSON
/// actions as the customer picks a barber/services/date — see wwwroot/js/booking.js.</summary>
public sealed class BookingPageViewModel
{
    public List<Service> Services { get; set; } = [];
    public List<Barber> Barbers { get; set; } = [];
    public string? ErrorMessage { get; set; }

    /// <summary>Set when arriving via a "Book" link from the Services or Team page
    /// (query string ?serviceId=... or ?barberId=...) so the form starts with that
    /// choice already made instead of the customer re-picking what they just clicked.</summary>
    public string? PreselectedServiceId { get; set; }
    public string? PreselectedBarberId { get; set; }
}
