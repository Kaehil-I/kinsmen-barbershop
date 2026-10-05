namespace Kinsmen.Api.Domain;

// Staff management: admins change other people's roles. Roles live in the identity provider (Auth0
// app_metadata.role) and reach the API as the token's "role" claim, so changing a role means updating
// the identity provider; the API sees a change when the person's access token is next issued, and the web
// app (whose session holds the role from sign-in) when they next sign in.

// A person as the identity provider knows them. Role is "Customer" when none has been assigned.
public sealed record IdentityUser(string UserId, string? Email, bool EmailVerified, string? Name, string Role);

public sealed record RoleChangeRequest(string Role);

// One audit record per role change: who changed whom, from what to what, and when.
public sealed record AuditEntry(string Id, string ActorId, string Action, string TargetUserId, string? TargetEmail,
    string FromRole, string ToRole, DateTime AtUtc);

public interface IIdentityDirectory
{
    // False when the identity provider's management credentials aren't configured; staff management is then off.
    bool IsConfigured { get; }
    Task<List<IdentityUser>> FindByEmail(string email, CancellationToken ct);
    Task<IdentityUser?> FindById(string userId, CancellationToken ct);
    Task<List<IdentityUser>> ListStaff(CancellationToken ct);
    Task SetRole(string userId, string role, CancellationToken ct);
}

public interface IAuditLog
{
    Task Record(AuditEntry entry, CancellationToken ct);
    Task<List<AuditEntry>> Recent(int count, CancellationToken ct);
}
