using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

public sealed class BookingTests
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private BookingService Service => new(store, clock, new());
    private static readonly Actor Customer = new("customer-a", "Customer");
    private static readonly Actor Staff = new("staff-a", "Barber");
    internal static DateTimeOffset Start => DateTimeOffset.Parse("2026-09-19T08:00:00Z");
    private static CreateBookingRequest Request(string? barber = "barber-a", DateTimeOffset? start = null, string[]? ids = null)
        => new(barber, ids ?? ["haircut", "beard"], start ?? Start);

    [Fact] public async Task MultiServiceTotalsAndUtcAreComputedOnServer()
    {
        var b = await Service.Create(Customer, Request(start: Start.ToOffset(TimeSpan.FromHours(2))));
        Assert.Equal(30000, b.TotalCents); Assert.Equal(TimeSpan.FromMinutes(45), b.EndUtc - b.StartUtc);
        Assert.Equal(DateTimeKind.Utc, b.StartUtc.Kind); Assert.Equal(BookingStatus.Pending, b.Status);
    }
    [Fact] public async Task DuplicateServiceIsRejected()
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(ids: ["haircut", "haircut"])))).Status);
    [Fact] public async Task UnknownServiceIsRejected()
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(ids: ["missing"])))).Status);
    [Fact] public async Task InactiveServiceIsRejected()
    {
        store.Catalog[0] = store.Catalog[0] with { Active = false };
        await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request()));
    }
    [Theory]
    [InlineData("2026-09-18T06:30:00Z")]
    [InlineData("2026-11-19T08:00:00Z")]
    [InlineData("2026-09-19T08:01:00Z")]
    public async Task NoticeHorizonAndSlotAlignmentAreEnforced(string start)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(start: DateTimeOffset.Parse(start))))).Status);
    [Theory]
    [InlineData("2026-09-20T08:00:00Z")]
    [InlineData("2026-09-19T14:30:00Z")]
    public async Task ClosedDayAndAppointmentsExtendingPastClosingAreRejected(string start)
        => Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(start: DateTimeOffset.Parse(start))))).Status);
    [Fact] public async Task AdjacentBookingsAreAllowedButPartialOverlapIsRejected()
    {
        await Service.Create(Customer, Request());
        await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(start: Start.AddMinutes(30))));
        await Service.Create(Customer, Request(start: Start.AddMinutes(45)));
        Assert.Equal(2, store.Saved.Count);
    }
    [Fact] public async Task AnyBarberSelectsAnotherAvailableBarber()
    {
        await Service.Create(Customer, Request());
        Assert.Equal("barber-b", (await Service.Create(Customer, Request(null))).BarberId);
    }
    [Fact] public async Task CustomerCannotBookForAnotherUser()
        => Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request() with { CustomerId = "victim" }))).Status);
    [Fact] public async Task CustomerCannotModifyOrListOthersBookings()
    {
        var b = await Service.Create(Customer, Request());
        var other = new Actor("customer-b", "Customer");
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Service.Cancel(other, b.Id, b.Version))).Status);
        Assert.Empty(await Service.List(other, Start, Start.AddDays(1), null));
    }
    [Fact] public async Task FailedReschedulePreservesOriginalAppointment()
    {
        var original = await Service.Create(Customer, Request());
        await Service.Create(Customer, Request(start: Start.AddHours(1)));
        await Assert.ThrowsAsync<DomainError>(() => Service.Reschedule(Customer, original.Id, new(Start.AddHours(1), 1)));
        Assert.Equal(original, await store.BookingById(original.Id));
    }
    [Fact] public async Task ReschedulePreservesPriceSnapshotAndReleasesOldTime()
    {
        var b = await Service.Create(Customer, Request());
        store.Catalog[0] = store.Catalog[0] with { PriceCents = 99999, DurationMinutes = 90 };
        var moved = await Service.Reschedule(Customer, b.Id, new(Start.AddHours(2), 1));
        Assert.Equal(30000, moved.TotalCents); Assert.Equal(TimeSpan.FromMinutes(45), moved.EndUtc - moved.StartUtc);
        Assert.Equal(2, moved.Version);
        Assert.Contains(await Service.Availability(new(2026, 9, 19), ["beard"], "barber-a"), x => x.StartUtc == Start.UtcDateTime);
    }
    [Fact] public async Task CancellationFreesTheTimeAndRejectsStaleUpdates()
    {
        var b = await Service.Create(Customer, Request());
        await Service.Cancel(Customer, b.Id, 1);
        Assert.Equal("stale_version", (await Assert.ThrowsAsync<DomainError>(() => Service.Reschedule(Customer, b.Id, new(Start.AddHours(1), 1)))).Code);
        await Service.Create(Customer, Request());
    }
    [Fact] public async Task LateCancellationIsRejected()
    {
        var b = await Service.Create(Customer, Request()); clock.Now = Start.AddMinutes(-30);
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => Service.Cancel(Customer, b.Id, 1))).Status);
    }
    [Fact] public async Task TimeBlockAffectsAvailabilityAndCannotOverwriteAppointments()
    {
        await Service.AddBlock(Staff, "barber-a", new(Start, Start.AddHours(1), "Break"));
        await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request()));
        Assert.DoesNotContain(await Service.Availability(new(2026, 9, 19), ["haircut"], "barber-a"), s => s.StartUtc == Start.UtcDateTime);
        await Service.Create(Customer, Request(start: Start.AddHours(1)));
        await Assert.ThrowsAsync<DomainError>(() => Service.AddBlock(Staff, "barber-a", new(Start.AddHours(1), Start.AddHours(2), "Break")));
    }
    [Fact] public async Task RemovingTimeBlockRestoresAvailability()
    {
        var block = await Service.AddBlock(Staff, "barber-a", new(Start, Start.AddHours(1), "Break"));
        await Service.RemoveBlock(Staff, block.Id);
        await Service.Create(Customer, Request());
    }
    [Fact] public async Task BarberCannotBlockAnotherBarber()
        => Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Service.AddBlock(Staff, "barber-b", new(Start, Start.AddHours(1), "Break")))).Status);
    [Fact] public async Task StatusTransitionsRequireCorrectStaffAndAppointmentTime()
    {
        var b = await Service.Create(Customer, Request());
        await Assert.ThrowsAsync<DomainError>(() => Service.ChangeStatus(Customer, b.Id, new(BookingStatus.Completed, 1)));
        await Assert.ThrowsAsync<DomainError>(() => Service.ChangeStatus(Staff, b.Id, new(BookingStatus.Completed, 1)));
        var confirmed = await Service.ChangeStatus(Staff, b.Id, new(BookingStatus.Confirmed, 1));
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
        await Assert.ThrowsAsync<DomainError>(() => Service.ChangeStatus(Staff, b.Id, new(BookingStatus.Completed, 2)));
        clock.Now = Start.AddHours(1);
        Assert.Equal(BookingStatus.Completed, (await Service.ChangeStatus(Staff, b.Id, new(BookingStatus.Completed, 2))).Status);
        await Assert.ThrowsAsync<DomainError>(() => Service.ChangeStatus(Staff, b.Id, new(BookingStatus.NoShow, 3)));
    }
    [Fact] public async Task DisabledBarberCannotManageTheirSchedule()
    {
        store.Staff[0] = store.Staff[0] with { Active = false };
        await Assert.ThrowsAsync<DomainError>(() => Service.List(Staff, Start, Start.AddDays(1), "barber-a"));
        await Assert.ThrowsAsync<DomainError>(() => Service.AddBlock(Staff, "barber-a", new(Start, Start.AddHours(1), "Break")));
    }
    [Theory]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public async Task ExtremeDatesAreValidationErrors(string date)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.Availability(DateOnly.Parse(date), ["haircut"], null))).Status);
    [Fact] public async Task PeriodEndingAtMidnightAllowsAnAppointmentEndingExactlyThen()
    {
        store.Staff[0] = store.Staff[0] with { Hours = [new(6, 1380, 1440)] };
        var b = await Service.Create(Customer, Request(start: DateTimeOffset.Parse("2026-09-19T23:15:00+02:00")));
        Assert.Equal(DateTime.Parse("2026-09-19T22:00:00Z").ToUniversalTime(), b.EndUtc);
    }
    [Fact] public async Task ExcessiveListRangeIsRejected()
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.List(Customer, Start, Start.AddDays(90), null))).Status);
    [Fact] public async Task BarberSeesBookedServicesAndCustomerNotes()
    {
        await Service.Create(Customer, Request() with { Notes = "  Please discuss beard length first.  " });
        var booking = Assert.Single(await Service.List(Staff, Start, Start.AddDays(1), "barber-a"));
        Assert.Equal("Please discuss beard length first.", booking.Notes);
        Assert.Equal(2, booking.Services.Length);
    }
    [Fact] public async Task ExcessiveNotesAreRejected()
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request() with { Notes = new string('a', 501) }))).Status);
    [Fact] public async Task RetriedAnyBarberRequestReturnsSameBookingEvenAfterCatalogueChanges()
    {
        const string key = "customer-request-0001";
        var b = await Service.Create(Customer, Request(null), idempotencyKey: key);
        store.Catalog[0] = store.Catalog[0] with { Active = false, PriceCents = 50000 };
        var retry = await Service.Create(Customer, Request(null), idempotencyKey: key);
        Assert.Equal(b.Id, retry.Id); Assert.Single(store.Saved); Assert.Equal(30000, retry.TotalCents);
    }
    [Fact] public async Task ReusedKeyWithDifferentPayloadIsRejected()
    {
        const string key = "customer-request-0002";
        await Service.Create(Customer, Request(), idempotencyKey: key);
        var error = await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(start: Start.AddHours(1)), idempotencyKey: key));
        Assert.Equal("idempotency_conflict", error.Code); Assert.Single(store.Saved);
    }
    [Fact] public async Task KeysAreScopedToTheAuthenticatedAccount()
    {
        const string key = "customer-request-0003";
        var a = await Service.Create(Customer, Request(), idempotencyKey: key);
        var b = await Service.Create(new("customer-b", "Customer"), Request("barber-b"), idempotencyKey: key);
        Assert.NotEqual(a.Id, b.Id); Assert.Equal(2, store.Saved.Count);
    }
    [Fact] public async Task ReplayAfterCancellationDoesNotCreateAnotherAppointment()
    {
        const string key = "customer-request-0004";
        var b = await Service.Create(Customer, Request(), idempotencyKey: key);
        await Service.Cancel(Customer, b.Id, 1);
        clock.Now = Start.AddDays(1);
        var retry = await Service.Create(Customer, Request(), idempotencyKey: key);
        Assert.Equal(BookingStatus.Cancelled, retry.Status); Assert.Equal(2, retry.Version); Assert.Single(store.Saved);
    }
    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("header key with spaces")]
    public async Task InvalidIdempotencyKeysAreRejected(string key)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Service.Create(Customer, Request(), idempotencyKey: key))).Status);
    [Fact] public async Task DetailsEndpointRulesRestrictAccessToOwnerAssignedBarberOrAdmin()
    {
        var b = await Service.Create(Customer, Request());
        Assert.Equal(b.Id, (await Service.Get(Customer, b.Id)).Id);
        Assert.Equal(b.Id, (await Service.Get(Staff, b.Id)).Id);
        Assert.Equal(b.Id, (await Service.Get(new("admin", "Admin"), b.Id)).Id);
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Service.Get(new("other", "Customer"), b.Id))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Service.Get(new("staff-b", "Barber"), b.Id))).Status);
    }
}
