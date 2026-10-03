using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

public sealed class CatalogueTests
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private CatalogueService Catalogue => new(store, clock, new());
    private BookingService Bookings => new(store, clock, new());
    private static readonly Actor Admin = new("admin-a", "Admin");
    private static readonly Actor Customer = new("customer-a", "Customer");
    private static readonly Actor Staff = new("staff-a", "Barber");
    // Monday to Saturday, 09:00-17:00 shop time, as in the demo data.
    private static WorkingPeriod[] WeekdayHours(int start = 540, int end = 1020)
        => Enumerable.Range(1, 6).Select(d => new WorkingPeriod(d, start, end)).ToArray();
    private static CreateBookingRequest Booking(string barber = "barber-a", string[]? services = null)
        => new(barber, services ?? ["haircut"], BookingTests.Start);

    [Fact] public async Task NonAdminsCannotReadOrChangeTheCatalogue()
    {
        foreach (var actor in new[] { Customer, Staff })
        {
            Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.AllServices(actor))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateService(actor, new("Fade", 25000, 30)))).Status);
            Assert.Equal(403, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.SetBarberActive(actor, "barber-a", false))).Status);
        }
    }

    [Fact] public async Task CreatedServiceIsListedAndBookable()
    {
        var fade = await Catalogue.CreateService(Admin, new("  Skin fade  ", 25000, 45));
        Assert.Equal("Skin fade", fade.Name);
        Assert.Contains(await Bookings.Services(default), s => s.Id == fade.Id);
        var booking = await Bookings.Create(Customer, Booking(services: [fade.Id]));
        Assert.Equal(25000, booking.TotalCents);
        Assert.Equal(TimeSpan.FromMinutes(45), booking.EndUtc - booking.StartUtc);
    }

    [Fact] public async Task PriceChangeDoesNotAlterExistingBookings()
    {
        var before = await Bookings.Create(Customer, Booking());
        await Catalogue.UpdateService(Admin, "haircut", new("Haircut", 99900, 60));
        var after = await Bookings.Get(Customer, before.Id);
        Assert.Equal(20000, after.TotalCents);
        Assert.Equal("Demo haircut", after.Services.Single().Name);
    }

    [Fact] public async Task DeactivatedServiceIsHiddenFromCustomersButKeptForAdmins()
    {
        await Catalogue.SetServiceActive(Admin, "haircut", false);
        Assert.DoesNotContain(await Bookings.Services(default), s => s.Id == "haircut");
        Assert.Contains(await Catalogue.AllServices(Admin), s => s.Id == "haircut" && !s.Active);
        await Assert.ThrowsAsync<DomainError>(() => Bookings.Create(Customer, Booking()));
        await Catalogue.SetServiceActive(Admin, "haircut", true);
        await Bookings.Create(Customer, Booking());
    }

    [Theory]
    [InlineData("", 10000, 30)]
    [InlineData("   ", 10000, 30)]
    [InlineData("Haircut", -1, 30)]
    [InlineData("Haircut", 1000001, 30)]
    [InlineData("Haircut", 10000, 0)]
    [InlineData("Haircut", 10000, 481)]
    public async Task InvalidServiceValuesAreRejected(string name, int price, int duration)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateService(Admin, new(name, price, duration)))).Status);

    [Fact] public async Task OverlongServiceNameIsRejected()
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateService(Admin, new(new string('x', 81), 10000, 30)))).Status);

    [Fact] public async Task ActiveServiceNamesMustBeUnique()
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateService(Admin, new("DEMO HAIRCUT", 10000, 30)));
        Assert.Equal("duplicate_name", error.Code);
        // An inactive service's name can be reused, but it cannot then be reactivated alongside the new one.
        await Catalogue.SetServiceActive(Admin, "haircut", false);
        await Catalogue.CreateService(Admin, new("Demo haircut", 10000, 30));
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.SetServiceActive(Admin, "haircut", true))).Status);
    }

    [Fact] public async Task UnknownRecordsReturnNotFound()
    {
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.UpdateService(Admin, "missing", new("X", 1, 1)))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.SetServiceActive(Admin, "missing", false))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.UpdateBarber(Admin, "missing", new("X", "u", WeekdayHours())))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.SetBarberActive(Admin, "missing", false))).Status);
    }

    [Fact] public async Task CreatedBarberIsBookableAndLinkedAccountCanManageSchedule()
    {
        var barber = await Catalogue.CreateBarber(Admin, new("Sipho", "auth0|sipho", WeekdayHours()));
        Assert.Contains(await Bookings.Barbers(default), b => b.Id == barber.Id);
        var booking = await Bookings.Create(Customer, Booking(barber.Id));
        var confirmed = await Bookings.ChangeStatus(new("auth0|sipho", "Barber"), booking.Id, new(BookingStatus.Confirmed, 1));
        Assert.Equal(BookingStatus.Confirmed, confirmed.Status);
    }

    [Fact] public async Task LinkedAccountCanOnlyBelongToOneBarber()
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateBarber(Admin, new("Copy", "staff-a", WeekdayHours())));
        Assert.Equal("duplicate_user", error.Code);
        Assert.Equal("duplicate_user", (await Assert.ThrowsAsync<DomainError>(
            () => Catalogue.UpdateBarber(Admin, "barber-b", new("Demo barber B", "staff-a", WeekdayHours())))).Code);
    }

    public static TheoryData<WorkingPeriod[]> InvalidHours => new()
    {
        new[] { new WorkingPeriod(0, 540, 1020) },
        new[] { new WorkingPeriod(8, 540, 1020) },
        new[] { new WorkingPeriod(1, 1020, 540) },
        new[] { new WorkingPeriod(1, 540, 540) },
        new[] { new WorkingPeriod(1, 540, 1441) },
        new[] { new WorkingPeriod(1, 540, 780), new WorkingPeriod(1, 720, 1020) },
        Enumerable.Range(0, 22).Select(i => new WorkingPeriod(1 + i % 7, i * 60, i * 60 + 30)).ToArray()
    };

    [Theory, MemberData(nameof(InvalidHours))]
    public async Task InvalidWorkingHoursAreRejected(WorkingPeriod[] hours)
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateBarber(Admin, new("New", "auth0|new", hours)))).Status);

    [Fact] public async Task SplitShiftsAndDaysOffAreAllowed()
    {
        var barber = await Catalogue.CreateBarber(Admin, new("Split", "auth0|split",
            [new(6, 780, 1020), new(6, 540, 720)]));
        Assert.Equal([new(6, 540, 720), new(6, 780, 1020)], barber.Hours);
        Assert.Empty((await Catalogue.CreateBarber(Admin, new("Off", "auth0|off", []))).Hours);
    }

    [Fact] public async Task HoursChangeCannotStrandUpcomingBookings()
    {
        await Bookings.Create(Customer, Booking());
        // Saturday 10:00 is booked; moving Saturday to an afternoon shift would leave it outside working hours.
        var error = await Assert.ThrowsAsync<DomainError>(() => Catalogue.UpdateBarber(Admin, "barber-a",
            new("Demo barber A", "staff-a", WeekdayHours(start: 780))));
        Assert.Equal(409, error.Status);
        Assert.Equal(540, store.Staff.Single(b => b.Id == "barber-a").Hours[0].StartMinute);
        // Changes that keep the booking inside the hours are fine.
        await Catalogue.UpdateBarber(Admin, "barber-a", new("Demo barber A", "staff-a", WeekdayHours(start: 480)));
    }

    [Fact] public async Task BarberWithUpcomingBookingsCannotBeDeactivatedUntilTheyAreCancelled()
    {
        var booking = await Bookings.Create(Customer, Booking());
        Assert.Equal(409, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.SetBarberActive(Admin, "barber-a", false))).Status);
        await Bookings.Cancel(Customer, booking.Id, booking.Version);
        await Catalogue.SetBarberActive(Admin, "barber-a", false);
        Assert.DoesNotContain(await Bookings.Barbers(default), b => b.Id == "barber-a");
        Assert.Contains(await Catalogue.AllBarbers(Admin), b => b.Id == "barber-a" && !b.Active);
        Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Bookings.Create(Customer, Booking()))).Status);
    }

    [Fact] public async Task PastBookingsDoNotBlockDeactivation()
    {
        await Bookings.Create(Customer, Booking());
        clock.Now = BookingTests.Start.AddDays(1);
        await Catalogue.SetBarberActive(Admin, "barber-a", false);
    }

    [Fact] public async Task ProfileEditsIncrementTheScheduleRevision()
    {
        var before = store.Staff.Single(b => b.Id == "barber-b").Revision;
        await Catalogue.UpdateBarber(Admin, "barber-b", new("Renamed", "staff-b", WeekdayHours()));
        Assert.True(store.Staff.Single(b => b.Id == "barber-b").Revision > before);
    }
}
