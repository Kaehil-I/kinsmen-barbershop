using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Body the admin page's JS posts to AdminController.SaveService. An empty Id
/// means "create"; a value means "update that service". Distinct from Models.Api.ServiceInput
/// because the API's own body has no id (it's in the URL) and rejects unknown fields.</summary>
public sealed class SaveServiceAjaxRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int DurationMinutes { get; set; }
}

/// <summary>Body the admin page's JS posts to AdminController.SaveBarber. Same
/// create-vs-update convention as SaveServiceAjaxRequest.</summary>
public sealed class SaveBarberAjaxRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<WorkingPeriod> Hours { get; set; } = [];
}
