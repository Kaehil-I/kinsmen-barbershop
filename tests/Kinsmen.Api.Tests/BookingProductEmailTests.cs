using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

/// <summary>Customers who reserve products with a booking see them in every email about it.</summary>
public sealed class BookingProductEmailTests
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private readonly RecordingEmailSender emails = new();
    private BookingService Service => new(store, clock, new(), emails);
    private static readonly Actor Customer = new("customer-a", "Customer", "a@example.com");
    private static readonly Actor Staff = new("staff-a", "Barber");

    private static CreateBookingRequest Request(params ProductSelection[] products)
        => new("barber-a", ["haircut"], BookingTests.Start, Products: products);

    [Fact] public async Task TheReceivedEmailListsTheReservedProductsAndTheTotal()
    {
        await Service.Create(Customer, Request(new ProductSelection("beard-oil", 2)));

        var mail = Assert.Single(emails.Sent);
        Assert.Contains("Reserved products", mail.Body);
        Assert.Contains("Beard Oil &times; 2", mail.Body);
        Assert.Contains("R700.00", mail.Body); // R200 haircut + 2 x R250 beard oil.
    }

    [Fact] public async Task TheConfirmedAndCancelledEmailsListThemToo()
    {
        var booking = await Service.Create(Customer, Request(new ProductSelection("matify", 1)));
        var confirmed = await Service.ChangeStatus(Staff, booking.Id, new(BookingStatus.Confirmed, booking.Version));
        await Service.Cancel(Customer, booking.Id, confirmed.Version);

        var subjects = emails.Sent.Select(m => m.Subject).ToList();
        Assert.Contains("Your booking is confirmed", subjects);
        Assert.Contains("Your booking has been cancelled", subjects);
        Assert.All(emails.Sent, mail => Assert.Contains("Matify &times; 1", mail.Body));
    }

    [Fact] public async Task TheRescheduledEmailKeepsListingThem()
    {
        var booking = await Service.Create(Customer, Request(new ProductSelection("beard-oil", 1)));
        emails.Sent.Clear();

        await Service.Reschedule(Customer, booking.Id, new(BookingTests.Start.AddHours(2), booking.Version));

        var mail = Assert.Single(emails.Sent);
        Assert.Equal("Your booking has been rescheduled", mail.Subject);
        Assert.Contains("Beard Oil &times; 1", mail.Body);
    }

    [Fact] public async Task TheReminderListsThemSoTheCustomerKnowsWhatToCollect()
    {
        var booking = await Service.Create(Customer, Request(new ProductSelection("beard-oil", 1)));
        await Service.ChangeStatus(Staff, booking.Id, new(BookingStatus.Confirmed, booking.Version));
        emails.Sent.Clear();
        clock.Now = BookingTests.Start.AddHours(-23);

        Assert.Equal(1, await Service.SendDueReminders());

        var mail = Assert.Single(emails.Sent);
        Assert.StartsWith("Reminder:", mail.Subject);
        Assert.Contains("Beard Oil &times; 1", mail.Body);
    }

    [Fact] public async Task NoProductsMeansNoProductsRow()
    {
        await Service.Create(Customer, Request());

        Assert.DoesNotContain("Reserved products", Assert.Single(emails.Sent).Body);
    }
}
