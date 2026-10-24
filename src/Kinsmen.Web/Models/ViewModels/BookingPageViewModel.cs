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
    /// (?serviceId=... or ?barberId=...) or a "Book again" link from My Bookings
    /// (?barberId=...&amp;serviceIds=...&amp;serviceIds=...), so the form starts with those
    /// choices already made instead of the customer re-picking them.</summary>
    public List<string> PreselectedServiceIds { get; set; } = [];
    public string? PreselectedBarberId { get; set; }

    /// <summary>Shown above the form when a "Book again" link asked for a service or
    /// barber that is no longer offered, so the customer knows why it isn't selected.</summary>
    public string? RebookNotice { get; set; }
}
