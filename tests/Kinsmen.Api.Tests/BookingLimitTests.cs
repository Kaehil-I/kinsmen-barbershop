using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

public sealed class BookingLimitTests
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private BookingService Service(int max = 3) => new(store, clock, new(MaxActiveBookingsPerCustomer: max));
    private static readonly Actor Customer = new("customer-a", "Customer");
    private static CreateBookingRequest At(int hoursAfterStart, string barber = "barber-a")
        => new(barber, ["haircut"], BookingTests.Start.AddHours(hoursAfterStart));

    private async Task<List<Booking>> BookThree()
        => [await Service().Create(Customer, At(0)), await Service().Create(Customer, At(1)), await Service().Create(Customer, At(2))];

    [Fact] public async Task FourthActiveBookingIsRefused()
    {
        await BookThree();
        var error = await Assert.ThrowsAsync<DomainError>(() => Service().Create(Customer, At(3)));
        Assert.Equal(409, error.Status);
        Assert.Equal("booking_limit", error.Code);
        Assert.Equal(3, store.Saved.Count);
    }

    [Fact] public async Task ConfirmedBookingsCountTowardsTheLimit()
    {
        var bookings = await BookThree();
        await Service().ChangeStatus(new("staff-a", "Barber"), bookings[0].Id, new(BookingStatus.Confirmed, 1));
        await Assert.ThrowsAsync<DomainError>(() => Service().Create(Customer, At(3)));
    }

    [Fact] public async Task CancellingFreesAPlace()
    {
        var bookings = await BookThree();
        await Service().Cancel(Customer, bookings[1].Id, bookings[1].Version);
        await Service().Create(Customer, At(3));
        Assert.Equal(4, store.Saved.Count);
    }

    [Fact] public async Task FinishedAppointmentsFreeAPlace()
    {
        await BookThree();
        // After all three have ended, the customer can book again.
        clock.Now = BookingTests.Start.AddHours(3);
        await Service().Create(Customer, new("barber-a", ["haircut"], BookingTests.Start.AddDays(2)));
    }

    [Fact] public async Task LimitIsPerCustomer()
    {
        await BookThree();
        await Service().Create(new("customer-b", "Customer"), At(3));
    }

    [Fact] public async Task AdminBookingOnACustomersBehalfCountsForThatCustomer()
    {
        await BookThree();
        var error = await Assert.ThrowsAsync<DomainError>(() => Service().Create(new("admin-a", "Admin"), At(3) with { CustomerId = "customer-a" }));
        Assert.Equal("booking_limit", error.Code);
    }

    [Fact] public async Task RetryingAnAlreadyCreatedBookingIsNotBlockedByTheLimit()
    {
        await Service().Create(Customer, At(0));
        await Service().Create(Customer, At(1));
        var third = await Service().Create(Customer, At(2), idempotencyKey: "limit-retry-key-0001");
        // The customer is now at the limit, but a retry of the request that created the third booking must still succeed.
        var replay = await Service().Create(Customer, At(2), idempotencyKey: "limit-retry-key-0001");
        Assert.Equal(third.Id, replay.Id);
    }

    [Fact] public async Task LimitIsConfigurable()
    {
        await Service(max: 1).Create(Customer, At(0));
        await Assert.ThrowsAsync<DomainError>(() => Service(max: 1).Create(Customer, At(1)));
    }

    [Fact] public async Task ReschedulingDoesNotCountAsANewBooking()
    {
        var bookings = await BookThree();
        var moved = await Service().Reschedule(Customer, bookings[2].Id, new(BookingTests.Start.AddHours(4), bookings[2].Version));
        Assert.Equal(BookingTests.Start.AddHours(4).UtcDateTime, moved.StartUtc);
    }
}
