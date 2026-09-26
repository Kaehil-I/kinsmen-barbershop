namespace Kinsmen.Api.Domain;

// Admin management of services and barber profiles. Nothing is deleted: deactivated records stay so that
// historical bookings keep valid references, and booked prices are already preserved in service snapshots.
public sealed class CatalogueService(IBookingStore store, TimeProvider clock, BookingPolicy policy)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public Task<List<ServiceItem>> AllServices(Actor actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return store.Read(async s => (await s.Services()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList(), ct);
    }

    public Task<ServiceItem> CreateService(Actor actor, ServiceRequest input, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var name = ValidateService(input);
        return store.Write(async s =>
        {
            await RequireUniqueName(s, name, null);
            var service = new ServiceItem(Guid.NewGuid().ToString("N"), name, input.PriceCents, input.DurationMinutes);
            await s.SaveService(service, true);
            return service;
        }, ct);
    }

    public Task<ServiceItem> UpdateService(Actor actor, string id, ServiceRequest input, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var name = ValidateService(input);
        return store.Write(async s =>
        {
            var existing = (await s.Services()).SingleOrDefault(x => x.Id == id) ?? throw DomainError.Missing();
            await RequireUniqueName(s, name, id);
            // Existing bookings keep the name, price and duration captured in their snapshots.
            var updated = existing with { Name = name, PriceCents = input.PriceCents, DurationMinutes = input.DurationMinutes };
            await s.SaveService(updated);
            return updated;
        }, ct);
    }

    public Task<ServiceItem> SetServiceActive(Actor actor, string id, bool active, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return store.Write(async s =>
        {
            var existing = (await s.Services()).SingleOrDefault(x => x.Id == id) ?? throw DomainError.Missing();
            if (active) await RequireUniqueName(s, existing.Name, id);
            var updated = existing with { Active = active };
            await s.SaveService(updated);
            return updated;
        }, ct);
    }

    public Task<List<BarberProfile>> AllBarbers(Actor actor, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return store.Read(async s => (await s.Barbers()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(Profile).ToList(), ct);
    }

    public async Task<BarberProfile> CreateBarber(Actor actor, BarberRequest input, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var (name, userId, hours) = ValidateBarber(input);
        try
        {
            return await store.Write(async s =>
            {
                if ((await s.Barbers()).Any(b => b.UserId == userId)) throw DuplicateUser();
                var barber = new Barber(Guid.NewGuid().ToString("N"), name, userId, hours);
                await s.SaveBarber(barber, true);
                return Profile(barber);
            }, ct);
        }
        catch (DuplicateBarberUserException) { throw DuplicateUser(); }
    }

    public async Task<BarberProfile> UpdateBarber(Actor actor, string id, BarberRequest input, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        var (name, userId, hours) = ValidateBarber(input);
        try
        {
            return await store.Write(async s =>
            {
                var existing = (await s.Barbers()).SingleOrDefault(b => b.Id == id) ?? throw DomainError.Missing();
                if ((await s.Barbers()).Any(b => b.Id != id && b.UserId == userId)) throw DuplicateUser();
                var updated = existing with { Name = name, UserId = userId, Hours = hours };
                // Lock the schedule before reading bookings so a concurrent booking cannot land outside the new hours.
                await s.TouchBarbers([id]);
                if (existing.Active && (await Upcoming(s, id)).Any(b => !BookingService.WithinHours(updated, b.StartUtc, b.EndUtc)))
                    throw DomainError.Conflict("Upcoming bookings fall outside the new working hours. Move or cancel them first.");
                await s.SaveBarber(updated);
                return Profile(updated);
            }, ct);
        }
        catch (DuplicateBarberUserException) { throw DuplicateUser(); }
    }

    public Task<BarberProfile> SetBarberActive(Actor actor, string id, bool active, CancellationToken ct = default)
    {
        RequireAdmin(actor);
        return store.Write(async s =>
        {
            var existing = (await s.Barbers()).SingleOrDefault(b => b.Id == id) ?? throw DomainError.Missing();
            await s.TouchBarbers([id]);
            if (!active && (await Upcoming(s, id)).Count != 0)
                throw DomainError.Conflict("This barber has upcoming bookings. Move or cancel them before deactivating.");
            var updated = existing with { Active = active };
            await s.SaveBarber(updated);
            return Profile(updated);
        }, ct);
    }

    // Pending or confirmed bookings that have not finished. Bookings cannot be made beyond the horizon.
    private async Task<List<Booking>> Upcoming(IBookingSession s, string barberId)
        => (await s.Bookings(null, barberId, Now, Now.AddDays(policy.HorizonDays + 1)))
            .Where(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed).ToList();

    private static async Task RequireUniqueName(IBookingSession s, string name, string? excludingId)
    {
        if ((await s.Services()).Any(x => x.Active && x.Id != excludingId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new DomainError(409, "duplicate_name", "An active service already uses this name.");
    }

    private static string ValidateService(ServiceRequest input)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80) throw DomainError.Invalid("Service name must be 1 to 80 characters.");
        if (input.PriceCents is < 0 or > 1000000) throw DomainError.Invalid("Price must be between R0 and R10 000.");
        if (input.DurationMinutes is < 1 or > 480) throw DomainError.Invalid("Duration must be between 1 and 480 minutes.");
        return name;
    }

    private static (string Name, string UserId, WorkingPeriod[] Hours) ValidateBarber(BarberRequest input)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80) throw DomainError.Invalid("Barber name must be 1 to 80 characters.");
        var userId = input.UserId?.Trim();
        if (string.IsNullOrEmpty(userId) || userId.Length > 128) throw DomainError.Invalid("Linked account ID must be 1 to 128 characters.");
        var hours = input.Hours ?? [];
        if (hours.Length > 21) throw DomainError.Invalid("Supply at most 21 working periods.");
        if (hours.Any(p => p is null || p.Weekday is < 1 or > 7 || p.StartMinute is < 0 or > 1439 || p.EndMinute > 1440 || p.EndMinute <= p.StartMinute))
            throw DomainError.Invalid("Each working period needs a weekday of 1 (Monday) to 7 (Sunday) and a start before its end within the same day.");
        foreach (var day in hours.GroupBy(p => p.Weekday))
        {
            var ordered = day.OrderBy(p => p.StartMinute).ToArray();
            for (var i = 1; i < ordered.Length; i++)
                if (ordered[i].StartMinute < ordered[i - 1].EndMinute) throw DomainError.Invalid("Working periods on the same day must not overlap.");
        }
        return (name, userId, hours.OrderBy(p => p.Weekday).ThenBy(p => p.StartMinute).ToArray());
    }

    private static BarberProfile Profile(Barber b) => new(b.Id, b.Name, b.UserId, b.Hours, b.Active);
    private static DomainError DuplicateUser() => new(409, "duplicate_user", "This account is already linked to another barber.");
    private static void RequireAdmin(Actor actor)
    {
        if (!actor.IsAdmin) throw DomainError.Forbidden();
    }
}
