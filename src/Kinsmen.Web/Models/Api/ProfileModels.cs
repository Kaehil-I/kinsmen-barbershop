namespace Kinsmen.Web.Models.Api;

/// <summary>A customer's own details (GET/PUT /api/me/profile). Every field is optional;
/// Phone comes back in one format, +27 followed by 9 digits.</summary>
public sealed class CustomerProfile
{
    public string Id { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? PreferredBarberId { get; set; }
}

public sealed class ProfileInput
{
    public string? DisplayName { get; set; }
    public string? Phone { get; set; }
    public string? PreferredBarberId { get; set; }
}
