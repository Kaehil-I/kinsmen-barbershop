using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Everything the admin catalogue page needs on first load. Both lists include
/// inactive records - the admin needs to see (and re-activate) those, unlike the public
/// pages.</summary>
public sealed class AdminCataloguePageViewModel
{
    public List<Service> Services { get; set; } = [];
    public List<AdminBarber> Barbers { get; set; } = [];
    public string? ErrorMessage { get; set; }
}
