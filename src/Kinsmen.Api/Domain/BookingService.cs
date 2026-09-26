using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kinsmen.Api.Domain;

public sealed class BookingService(IBookingStore store, TimeProvider clock, BookingPolicy policy)
{
    // Kinsmen's shop timezone is explicit, independent of the host machine timezone.
    private static readonly TimeZoneInfo ShopZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static bool Overlaps(DateTime start, DateTime end, DateTime otherStart, DateTime otherEnd)
        => start < otherEnd && end > otherStart;
    private static bool Occupies(Booking b) => b.Status != BookingStatus.Cancelled;

    public Task<List<ServiceItem>> Services(CancellationToken ct) => store.Read(async s =>
        (await s.Services()).Where(x => x.Active).ToList(), ct);
    public Task<List<Barber>> Barbers(CancellationToken ct) => store.Read(async s =>
        (await s.Barbers()).Where(x => x.Active).ToList(), ct);

    private static ServiceSnapshot[] SelectServices(List<ServiceItem> catalog, string[]? ids)
    {
        ValidateServiceIds(ids);
        var selected = ids!.Select(id => catalog.SingleOrDefault(s => s.Id == id && s.Active)
            ?? throw DomainError.Invalid("A selected service is unavailable.")).ToArray();
        if (selected.Any(s => s.PriceCents < 0 || s.PriceCents > 1000000 || s.DurationMinutes is < 1 or > 480)
            || selected.Sum(s => s.DurationMinutes) > 480)
            throw DomainError.Invalid("Selected services exceed the supported appointment duration or price.");
        return selected.Select(s => new ServiceSnapshot(s.Id, s.Name, s.PriceCents, s.DurationMinutes)).ToArray();
    }

    private static void ValidateServiceIds(string[]? ids)
    {
        if (ids is null || ids.Length is < 1 or > 10 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct().Count() != ids.Length)
            throw DomainError.Invalid("Select 1 to 10 distinct service IDs.");
    }

    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static Booking Replay(Booking existing, string? fingerprint)
    {
        if (existing.CreationFingerprint != fingerprint)
            throw new DomainError(409, "idempotency_conflict", "This Idempotency-Key was already used for a different booking request.");
        return existing;
    }

    private void ValidateStart(DateTime start)
    {
        if (start < Now.AddMinutes(policy.MinimumNoticeMinutes) || start > Now.AddDays(policy.HorizonDays))
            throw DomainError.Invalid($"Start must be at least {policy.MinimumNoticeMinutes} minutes ahead and within {policy.HorizonDays} days.");
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, ShopZone);
        if (local.Second != 0 || local.Ticks % TimeSpan.TicksPerSecond != 0 || (local.Hour * 60 + local.Minute) % policy.SlotMinutes != 0)
            throw DomainError.Invalid($"Start must align to a {policy.SlotMinutes}-minute slot.");
    }

    internal static bool WithinHours(Barber barber, DateTime start, DateTime end)
    {
        var a = TimeZoneInfo.ConvertTimeFromUtc(start, ShopZone);
        var b = TimeZoneInfo.ConvertTimeFromUtc(end, ShopZone);
        var weekday = a.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)a.DayOfWeek;
        return barber.Active && barber.Hours.Any(p => p.Weekday == weekday
            && a >= a.Date.AddMinutes(p.StartMinute) && b <= a.Date.AddMinutes(p.EndMinute));
    }

    private static async Task<bool> Free(IBookingSession s, Barber barber, DateTime start, DateTime end, string? excluding = null)
    {
        if (!WithinHours(barber, start, end)) return false;
        var bookings = await s.Bookings(null, barber.Id, start, end);
        var blocks = await s.Blocks(barber.Id, start, end);
        return !bookings.Any(b => b.Id != excluding && Occupies(b) && Overlaps(start, end, b.StartUtc, b.EndUtc))
            && !blocks.Any(b => Overlaps(start, end, b.StartUtc, b.EndUtc));
    }

    public async Task<Booking> Create(Actor actor, CreateBookingRequest input, CancellationToken ct = default, string? idempotencyKey = null)
    {
        if (!actor.IsCustomer && !actor.IsAdmin) throw DomainError.Forbidden();
        if (!actor.IsAdmin && input.CustomerId is not null && input.CustomerId != actor.UserId) throw DomainError.Forbidden();
        var customer = actor.IsAdmin ? input.CustomerId : actor.UserId;
        if (string.IsNullOrWhiteSpace(customer) || customer.Length > 128) throw DomainError.Invalid("A valid customer ID is required.");
        if (input.Notes?.Length > 500) throw DomainError.Invalid("Booking notes must be no longer than 500 characters.");
        ValidateServiceIds(input.ServiceIds);
        if (idempotencyKey is not null && (idempotencyKey.Length is < 16 or > 128
            || idempotencyKey.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))))
            throw DomainError.Invalid("Idempotency-Key must contain 16-128 ASCII letters, digits, hyphens or underscores.");
        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        var start = input.Start.UtcDateTime;
        var bookingId = idempotencyKey is null ? Guid.NewGuid().ToString("N") : Hash(new { actor.UserId, Key = idempotencyKey });
        var fingerprint = idempotencyKey is null ? null : Hash(new
        {
            CustomerId = customer, input.BarberId, Services = input.ServiceIds.Order(StringComparer.Ordinal).ToArray(),
            StartUtcTicks = start.Ticks, Notes = notes
        });
        try
        {
            return await store.Write(async s =>
            {
                if (idempotencyKey is not null && await s.BookingById(bookingId) is { } previous)
                    return Replay(previous, fingerprint);
                ValidateStart(start);
                var candidates = (await s.Barbers()).Where(b => b.Active && (input.BarberId is null || b.Id == input.BarberId))
                    .OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
                if (candidates.Count == 0) throw DomainError.Invalid("No active matching barber exists.");
                // Every schedule mutation writes the same barber document before querying overlaps.
                // Concurrent transactions therefore conflict and retry against the committed schedule.
                await s.TouchBarbers(candidates.Select(b => b.Id));
                var services = SelectServices(await s.Services(), input.ServiceIds);
                var end = start.AddMinutes(services.Sum(x => x.DurationMinutes));
                foreach (var barber in candidates)
                {
                    if (!await Free(s, barber, start, end)) continue;
                    var booking = new Booking(bookingId, customer, barber.Id, start, end,
                        services, services.Sum(x => x.PriceCents), BookingStatus.Pending, Now,
                        Notes: notes, CreationFingerprint: fingerprint);
                    await s.SaveBooking(booking, true);
                    return booking;
                }
                throw DomainError.Conflict("That time is no longer available. Choose another slot.");
            }, ct);
        }
        catch (DuplicateBookingIdException) when (idempotencyKey is not null)
        {
            // Different payloads can lock different barbers yet use the same key.
            // The unique _id selects one winner; read it outside the aborted transaction.
            var existing = await store.Read(s => s.BookingById(bookingId), ct);
            if (existing is null) throw DomainError.Conflict("The booking request raced another update. Retry using the same key.");
            return Replay(existing, fingerprint);
        }
    }

    public Task<Booking> Get(Actor actor, string id, CancellationToken ct = default)
        => store.Read(async s =>
        {
            var booking = await s.BookingById(id) ?? throw DomainError.Missing();
            if (actor.IsCustomer || actor.IsAdmin) CanManage(actor, booking);
            else
            {
                var barber = (await s.Barbers()).Single(b => b.Id == booking.BarberId);
                StaffAccess(actor, barber);
            }
            return booking;
        }, ct);

    private static void CanManage(Actor actor, Booking booking)
    {
        if (!actor.IsAdmin && (!actor.IsCustomer || booking.CustomerId != actor.UserId)) throw DomainError.Missing();
    }
    private void CanChange(Booking booking, long version)
    {
        if (booking.Version != version) throw new DomainError(409, "stale_version", "The booking changed. Refresh before trying again.");
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed))
            throw DomainError.Conflict("Only pending or confirmed bookings can be changed.");
        if (booking.StartUtc < Now.AddMinutes(policy.CancellationNoticeMinutes))
            throw DomainError.Conflict("The cancellation or rescheduling notice period has passed.");
    }

    public Task<Booking> Reschedule(Actor actor, string id, RescheduleRequest input, CancellationToken ct = default)
        => store.Write(async s =>
        {
            var original = await s.BookingById(id) ?? throw DomainError.Missing();
            CanManage(actor, original);
            CanChange(original, input.Version);
            var start = input.Start.UtcDateTime;
            ValidateStart(start);
            await s.TouchBarbers([original.BarberId]);
            var barber = (await s.Barbers()).Single(b => b.Id == original.BarberId);
            var end = start.AddMinutes(original.Services.Sum(x => x.DurationMinutes));
            if (!await Free(s, barber, start, end, id)) throw DomainError.Conflict("The new time is unavailable; your original booking is unchanged.");
            var updated = original with { StartUtc = start, EndUtc = end, Version = original.Version + 1 };
            await s.SaveBooking(updated);
            return updated;
        }, ct);

    public Task<Booking> Cancel(Actor actor, string id, long version, CancellationToken ct = default)
        => store.Write(async s =>
        {
            var original = await s.BookingById(id) ?? throw DomainError.Missing();
            CanManage(actor, original);
            CanChange(original, version);
            await s.TouchBarbers([original.BarberId]);
            var updated = original with { Status = BookingStatus.Cancelled, Version = original.Version + 1 };
            await s.SaveBooking(updated);
            return updated;
        }, ct);

    public Task<Booking> ChangeStatus(Actor actor, string id, StatusRequest input, CancellationToken ct = default)
        => store.Write(async s =>
        {
            var booking = await s.BookingById(id) ?? throw DomainError.Missing();
            var barber = (await s.Barbers()).Single(b => b.Id == booking.BarberId);
            StaffAccess(actor, barber);
            if (booking.Version != input.Version) throw new DomainError(409, "stale_version", "Refresh this booking before changing it.");
            if (booking.Status == BookingStatus.Pending && input.Status == BookingStatus.Confirmed)
            {
                if (Now >= booking.StartUtc) throw DomainError.Conflict("Confirm a pending booking before its start time.");
            }
            else if (booking.Status == BookingStatus.Confirmed && input.Status is BookingStatus.Completed or BookingStatus.NoShow)
            {
                if (Now < (input.Status == BookingStatus.Completed ? booking.EndUtc : booking.StartUtc))
                    throw DomainError.Conflict("The appointment has not reached the required time for this status.");
            }
            else throw DomainError.Conflict("Allowed staff transitions are Pending to Confirmed, and Confirmed to Completed or NoShow.");
            await s.TouchBarbers([barber.Id]);
            var updated = booking with { Status = input.Status, Version = booking.Version + 1 };
            await s.SaveBooking(updated);
            return updated;
        }, ct);

    public Task<List<Booking>> List(Actor actor, DateTimeOffset from, DateTimeOffset to, string? barberId, CancellationToken ct = default)
        => store.Read(async s =>
        {
            ValidateRange(from, to);
            if (actor.IsCustomer) return await s.Bookings(actor.UserId, barberId, from.UtcDateTime, to.UtcDateTime);
            if (actor.IsAdmin) return await s.Bookings(null, barberId, from.UtcDateTime, to.UtcDateTime);
            var barber = (await s.Barbers()).SingleOrDefault(b => b.UserId == actor.UserId);
            if (!actor.IsBarber || barber is null || (barberId is not null && barberId != barber.Id)) throw DomainError.Forbidden();
            StaffAccess(actor, barber);
            return await s.Bookings(null, barber.Id, from.UtcDateTime, to.UtcDateTime);
        }, ct);

    public Task<List<AvailableSlot>> Availability(DateOnly date, string[] serviceIds, string? barberId, CancellationToken ct = default)
        => store.Read(async s =>
        {
            var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, ShopZone));
            if (date < firstDate || date > firstDate.AddDays(policy.HorizonDays))
                throw DomainError.Invalid("Date is outside the booking horizon.");
            var midnight = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), ShopZone);
            var duration = SelectServices(await s.Services(), serviceIds).Sum(x => x.DurationMinutes);
            var candidates = (await s.Barbers()).Where(b => b.Active && (barberId is null || b.Id == barberId)).ToList();
            var result = new List<AvailableSlot>();
            foreach (var barber in candidates)
            {
                // Fetch each day's data once rather than querying for every slot.
                var bookings = await s.Bookings(null, barber.Id, midnight, midnight.AddDays(1));
                var blocks = await s.Blocks(barber.Id, midnight, midnight.AddDays(1));
                for (var minute = 0; minute < 1440; minute += policy.SlotMinutes)
                {
                    var start = midnight.AddMinutes(minute);
                    var end = start.AddMinutes(duration);
                    if (start < Now.AddMinutes(policy.MinimumNoticeMinutes) || start > Now.AddDays(policy.HorizonDays) || !WithinHours(barber, start, end)) continue;
                    if (bookings.Any(b => Occupies(b) && Overlaps(start, end, b.StartUtc, b.EndUtc)) || blocks.Any(b => Overlaps(start, end, b.StartUtc, b.EndUtc))) continue;
                    result.Add(new AvailableSlot(barber.Id, start, end));
                }
            }
            return result.OrderBy(x => x.StartUtc).ThenBy(x => x.BarberId, StringComparer.Ordinal).ToList();
        }, ct);

    public Task<TimeBlock> AddBlock(Actor actor, string barberId, BlockRequest input, CancellationToken ct = default)
        => store.Write(async s =>
        {
            var barber = (await s.Barbers()).SingleOrDefault(b => b.Id == barberId) ?? throw DomainError.Missing();
            StaffAccess(actor, barber);
            var start = input.Start.UtcDateTime; var end = input.End.UtcDateTime;
            if (start < Now || end <= start || end - start > TimeSpan.FromDays(31) || end > Now.AddDays(policy.HorizonDays + 1))
                throw DomainError.Invalid("Time block must be future-dated, positive and within the booking horizon.");
            if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 200) throw DomainError.Invalid("Supply a reason of 1 to 200 characters.");
            await s.TouchBarbers([barberId]);
            if ((await s.Bookings(null, barberId, start, end)).Any(Occupies) || (await s.Blocks(barberId, start, end)).Count != 0)
                throw DomainError.Conflict("The block overlaps an appointment or another block.");
            var block = new TimeBlock(Guid.NewGuid().ToString("N"), barberId, start, end, input.Reason.Trim());
            await s.SaveBlock(block);
            return block;
        }, ct);

    public Task<bool> RemoveBlock(Actor actor, string id, CancellationToken ct = default)
        => store.Write(async s =>
        {
            var block = await s.BlockById(id) ?? throw DomainError.Missing();
            var barber = (await s.Barbers()).Single(b => b.Id == block.BarberId);
            StaffAccess(actor, barber);
            await s.TouchBarbers([barber.Id]);
            await s.DeleteBlock(id);
            return true;
        }, ct);

    public Task<List<TimeBlock>> ListBlocks(Actor actor, string barberId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
        => store.Read(async s =>
        {
            ValidateRange(from, to);
            var barber = (await s.Barbers()).SingleOrDefault(b => b.Id == barberId) ?? throw DomainError.Missing();
            StaffAccess(actor, barber);
            return await s.Blocks(barberId, from.UtcDateTime, to.UtcDateTime);
        }, ct);

    private static void StaffAccess(Actor actor, Barber barber)
    {
        if (!actor.IsAdmin && (!actor.IsBarber || !barber.Active || barber.UserId != actor.UserId)) throw DomainError.Forbidden();
    }
    private static void ValidateRange(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from || to - from > TimeSpan.FromDays(31)) throw DomainError.Invalid("Use a positive date range of at most 31 days.");
    }
}
