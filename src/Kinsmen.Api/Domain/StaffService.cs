namespace Kinsmen.Api.Domain;

// Rules for changing roles. Admins can promote people (including to Admin) and move people between Customer
// and Barber, but can't demote an Admin: that stays a deliberate action in the identity provider's dashboard,
// so one compromised admin account can't lock the others out, and two admins can never race to remove each
// other and leave the shop with none.
public sealed class StaffService(IIdentityDirectory directory, IAuditLog audit, IBookingStore store, TimeProvider clock,
    BookingPolicy policy)
{
    public static readonly string[] Roles = ["Customer", "Barber", "Admin"];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public bool IsEnabled(Actor actor)
    {
        RequireAdmin(actor);
        return directory.IsConfigured;
    }

    public async Task<List<IdentityUser>> ListStaff(Actor actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        RequireConfigured();
        return (await directory.ListStaff(ct))
            .OrderByDescending(u => Array.IndexOf(Roles, u.Role)).ThenBy(u => u.Email, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<IdentityUser>> FindByEmail(Actor actor, string? email, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        RequireConfigured();
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 254 || trimmed.Count(c => c == '@') != 1
            || trimmed.StartsWith('@') || trimmed.EndsWith('@'))
            throw DomainError.Invalid("Enter the email address the person signed up with.");
        return await directory.FindByEmail(trimmed, ct);
    }

    public async Task<IdentityUser> ChangeRole(Actor actor, string userId, string? role, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        RequireConfigured();
        if (role is null || !Roles.Contains(role, StringComparer.Ordinal))
            throw DomainError.Invalid("Role must be Customer, Barber or Admin.");
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128) throw DomainError.Missing();
        if (userId == actor.UserId)
            throw new DomainError(409, "own_role", "You can't change your own role. Ask another admin.");

        var target = await directory.FindById(userId, ct) ?? throw DomainError.Missing();
        if (target.Role == role) return target;
        if (target.Role == "Admin")
            throw new DomainError(409, "admin_demotion_not_allowed",
                "Admins can't be demoted here. The project owner can change an admin's role in the Auth0 dashboard.");
        if (role != "Customer" && !target.EmailVerified)
            throw new DomainError(409, "email_not_verified",
                "This account hasn't verified its email address yet. Ask them to verify it, then try again.");
        if (target.Role == "Barber" && role == "Customer") await RequireNoUpcomingBarberBookings(target.UserId, ct);

        await directory.SetRole(target.UserId, role, ct);
        await audit.Record(new AuditEntry(Guid.NewGuid().ToString("N"), actor.UserId, "role_changed", target.UserId,
            target.Email, target.Role, role, Now), ct);
        return target with { Role = role };
    }

    public Task<List<AuditEntry>> RecentChanges(Actor actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return audit.Recent(50, ct);
    }

    // Demoting a barber whose linked, active profile still has appointments would leave nobody able to
    // confirm or complete them, the same reasoning as refusing to deactivate that barber.
    private Task RequireNoUpcomingBarberBookings(string userId, CancellationToken ct) => store.Read(async s =>
    {
        foreach (var barber in (await s.Barbers()).Where(b => b.Active && b.UserId == userId))
        {
            var upcoming = (await s.Bookings(null, barber.Id, Now, Now.AddDays(policy.HorizonDays + 1)))
                .Any(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed);
            if (upcoming)
                throw new DomainError(409, "barber_has_bookings",
                    $"{barber.Name} still has upcoming bookings. Move or cancel them, or deactivate the barber profile, first.");
        }
        return true;
    }, ct);

    private void RequireConfigured()
    {
        if (!directory.IsConfigured)
            throw new DomainError(503, "staff_management_disabled",
                "Staff management isn't switched on yet: the Auth0 management credentials aren't configured.");
    }

    private static void RequireAdmin(Actor actor)
    {
        if (!actor.IsAdmin) throw DomainError.Forbidden();
    }
}
