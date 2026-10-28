using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

public sealed class DashboardPageViewModel
{
    public required DashboardPeriod Period { get; init; }
    public DashboardReport? Report { get; set; }
    public string? ErrorMessage { get; set; }
}
