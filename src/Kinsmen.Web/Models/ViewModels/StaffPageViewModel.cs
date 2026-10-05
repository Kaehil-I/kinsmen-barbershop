using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Staff accounts page. Enabled is false until the API has Auth0 management credentials;
/// the page then explains that instead of offering actions that would fail.</summary>
public sealed class StaffPageViewModel
{
    public bool Enabled { get; set; }
    public List<StaffMember> Staff { get; set; } = [];
    public List<StaffAuditEntry> RecentChanges { get; set; } = [];
    public string? CurrentUserId { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class SetRoleAjaxRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
