using System.Text.RegularExpressions;

namespace Kinsmen.Api.Domain;

// Customers read and update only their own profile: the id always comes from the caller's token, never from the
// request, so one customer can't read or overwrite another's. Staff have no customer profile.
public sealed partial class ProfileService(IProfileStore profiles, IBookingStore store, TimeProvider clock)
{
    public const int MaxDisplayNameLength = 60;

    public async Task<CustomerProfile> Get(Actor actor, CancellationToken ct)
    {
        RequireCustomer(actor);
        return await profiles.Get(actor.UserId, ct) ?? Empty(actor.UserId);
    }

    public async Task<CustomerProfile> Save(Actor actor, ProfileRequest input, CancellationToken ct)
    {
        RequireCustomer(actor);

        var displayName = Clean(input.DisplayName);
        if (displayName is { Length: > MaxDisplayNameLength })
            throw DomainError.Invalid($"Keep your name to {MaxDisplayNameLength} characters or fewer.");

        var phone = NormalisePhone(Clean(input.Phone));

        var preferredBarberId = Clean(input.PreferredBarberId);
        if (preferredBarberId is not null)
        {
            var active = await store.Read(async s => (await s.Barbers()).Any(b => b.Id == preferredBarberId && b.Active), ct);
            if (!active) throw DomainError.Invalid("Choose a barber who is currently taking bookings.");
        }

        var profile = new CustomerProfile(actor.UserId, displayName, phone, preferredBarberId, clock.GetUtcNow().UtcDateTime);
        await profiles.Save(profile, ct);
        return profile;
    }

    // South African numbers only, stored in one format (+27 followed by 9 digits) so they can be used for
    // reminders later. Accepts 0821234567, 082 123 4567, +27 82 123 4567 or 27821234567.
    public static string? NormalisePhone(string? phone)
    {
        if (phone is null) return null;
        var digits = NonDigits().Replace(phone, "");
        var national = digits switch
        {
            { Length: 10 } when digits.StartsWith('0') => digits[1..],
            { Length: 11 } when digits.StartsWith("27") => digits[2..],
            _ => null
        };
        if (national is null || national.StartsWith('0') || (phone.Contains('+') && !phone.TrimStart().StartsWith("+27")))
            throw DomainError.Invalid("Enter a South African phone number, for example 082 123 4567.");
        return "+27" + national;
    }

    private static void RequireCustomer(Actor actor)
    {
        if (!actor.IsCustomer) throw DomainError.Forbidden();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CustomerProfile Empty(string userId) => new(userId, null, null, null, DateTime.MinValue);

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex NonDigits();
}
