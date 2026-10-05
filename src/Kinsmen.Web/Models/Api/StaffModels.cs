namespace Kinsmen.Web.Models.Api;

/// <summary>A person's account as /api/admin/staff returns it. UserId is the Auth0 user ID
/// (e.g. auth0|65f0...), the same value a barber profile's linked account uses.</summary>
public sealed class StaffMember
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? Name { get; set; }
    public string Role { get; set; } = "Customer";
}

/// <summary>One role change from the API's audit log.</summary>
public sealed class StaffAuditEntry
{
    public string ActorId { get; set; } = string.Empty;
    public string TargetUserId { get; set; } = string.Empty;
    public string? TargetEmail { get; set; }
    public string FromRole { get; set; } = string.Empty;
    public string ToRole { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
}

public sealed class StaffStatus
{
    public bool Enabled { get; set; }
}

public sealed class RoleChangeInput
{
    public string Role { get; set; } = string.Empty;
}
