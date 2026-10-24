using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>The Profile page: the customer's editable details plus their recent visits.
/// The details and the history load separately, so a failure in one still shows the other.</summary>
public sealed class ProfilePageViewModel
{
    public ProfileForm Form { get; set; } = new();
    public string? Email { get; set; }
    public List<Barber> Barbers { get; set; } = [];

    public List<Booking> History { get; set; } = [];
    public Dictionary<string, string> BarberNamesById { get; set; } = [];

    /// <summary>The profile itself couldn't load, so the form isn't offered.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>The last save was refused; the form keeps what the customer typed.</summary>
    public string? SaveError { get; set; }
    public bool Saved { get; set; }
    public string? HistoryError { get; set; }
}

/// <summary>What the profile form posts back.</summary>
public sealed class ProfileForm
{
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? PreferredBarberId { get; set; }
}
