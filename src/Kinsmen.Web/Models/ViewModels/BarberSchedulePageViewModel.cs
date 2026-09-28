using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Everything the barber schedule page needs on first load: the barber's own
/// bookings (a few days back through the next four weeks - see ScheduleWindow for why the
/// window starts in the past) and their time blocks. MyBarberId/MyBarberName come from
/// BarberIdentityResolver, so they're known even when the barber has no bookings yet.
/// For an admin they stay null: an admin owns no barber profile, and instead sees every
/// barber's bookings - BarberNamesById is what lets each card say whose it is.</summary>
public sealed class BarberSchedulePageViewModel
{
    public List<Booking> Bookings { get; set; } = [];
    public List<TimeBlock> Blocks { get; set; } = [];
    public string? MyBarberId { get; set; }
    public string? MyBarberName { get; set; }
    public bool IsAdmin { get; set; }

    /// <summary>Barber ID to name, filled in for admins only (a barber's bookings are all
    /// their own, so there's nothing to label).</summary>
    public Dictionary<string, string> BarberNamesById { get; set; } = [];

    public string? ErrorMessage { get; set; }
}
