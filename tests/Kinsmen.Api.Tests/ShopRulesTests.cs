using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

/// <summary>Shop rules that sit between products and bookings: the last item, duplicate names, and moving a booking
/// that has products on it.</summary>
public sealed class ShopRulesTests
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private CatalogueService Catalogue => new(store, clock, new());
    private BookingService Bookings => new(store, clock, new());
    private static readonly Actor Admin = new("admin-a", "Admin");
    private static readonly Actor Alice = new("customer-a", "Customer");
    private static readonly Actor Bob = new("customer-b", "Customer");

    private int StockOf(string id) => store.Inventory.Single(p => p.Id == id).StockQuantity;

    private static CreateBookingRequest Book(string barber, DateTimeOffset start, params ProductSelection[] products)
        => new(barber, ["haircut"], start, Products: products);

    [Fact] public async Task OnlyOneOfTwoCustomersGetsTheLastItem()
    {
        store.Inventory[store.Inventory.FindIndex(p => p.Id == "matify")] = store.Inventory.Single(p => p.Id == "matify") with { StockQuantity = 1 };

        var first = await Bookings.Create(Alice, Book("barber-a", BookingTests.Start, new ProductSelection("matify", 1)));
        var second = await Assert.ThrowsAsync<DomainError>(() =>
            Bookings.Create(Bob, Book("barber-b", BookingTests.Start, new ProductSelection("matify", 1))));

        Assert.Equal(BookingStatus.Pending, first.Status);
        Assert.Equal("insufficient_stock", second.Code);
        Assert.Equal(0, StockOf("matify"));
        Assert.Single(store.Saved); // The refused booking left nothing behind, not even a half-made one.
    }

    [Fact] public async Task TwoCustomersBookingTheLastItemAtTheSameTimeCannotBothHaveIt()
    {
        store.Inventory[store.Inventory.FindIndex(p => p.Id == "matify")] = store.Inventory.Single(p => p.Id == "matify") with { StockQuantity = 1 };

        var results = await Task.WhenAll(
            Attempt(() => Bookings.Create(Alice, Book("barber-a", BookingTests.Start, new ProductSelection("matify", 1)))),
            Attempt(() => Bookings.Create(Bob, Book("barber-b", BookingTests.Start, new ProductSelection("matify", 1)))));

        Assert.Equal(1, results.Count(r => r));
        Assert.Equal(0, StockOf("matify"));
    }

    private static async Task<bool> Attempt(Func<Task<Booking>> action)
    {
        try { await action(); return true; }
        catch (DomainError) { return false; }
    }

    [Fact] public async Task RescheduleKeepsTheReservedProductsAndDoesNotTouchStock()
    {
        var before = StockOf("beard-oil");
        var booking = await Bookings.Create(Alice, Book("barber-a", BookingTests.Start, new ProductSelection("beard-oil", 2)));
        Assert.Equal(before - 2, StockOf("beard-oil"));

        var moved = await Bookings.Reschedule(Alice, booking.Id, new(BookingTests.Start.AddHours(2), booking.Version));

        Assert.Equal(booking.Products!.Single().ProductId, moved.Products!.Single().ProductId);
        Assert.Equal(2, moved.Products!.Single().Quantity);
        Assert.Equal(booking.TotalCents, moved.TotalCents);
        Assert.Equal(before - 2, StockOf("beard-oil")); // Still held once, not taken again and not given back.
    }

    [Fact] public async Task CancellingAfterARescheduleReturnsTheStockOnce()
    {
        var before = StockOf("beard-oil");
        var booking = await Bookings.Create(Alice, Book("barber-a", BookingTests.Start, new ProductSelection("beard-oil", 2)));
        var moved = await Bookings.Reschedule(Alice, booking.Id, new(BookingTests.Start.AddHours(2), booking.Version));

        await Bookings.Cancel(Alice, moved.Id, moved.Version);

        Assert.Equal(before, StockOf("beard-oil"));
    }

    [Fact] public async Task ANameAlreadyUsedByAnActiveProductIsRejectedIgnoringCase()
    {
        var error = await Assert.ThrowsAsync<DomainError>(() =>
            Catalogue.CreateProduct(Admin, new("beard OIL", 25000, 5)));

        Assert.Equal("duplicate_name", error.Code);
        Assert.Equal(409, error.Status);
        Assert.Single(store.Inventory, p => p.Name.Equals("Beard Oil", StringComparison.OrdinalIgnoreCase));
    }

    [Fact] public async Task RenamingAProductToAnotherOnesNameIsRejectedButKeepingItsOwnNameIsFine()
    {
        await Catalogue.UpdateProduct(Admin, "beard-oil", new("Beard Oil", 26000, null));          // Own name: fine.

        var error = await Assert.ThrowsAsync<DomainError>(() =>
            Catalogue.UpdateProduct(Admin, "beard-oil", new("Matify", 26000, null)));

        Assert.Equal("duplicate_name", error.Code);
    }

    [Fact] public async Task ANameFreedByDeactivatingCanBeUsedAgainButTheOldOneCannotComeBack()
    {
        await Catalogue.SetProductActive(Admin, "beard-oil", false);
        var replacement = await Catalogue.CreateProduct(Admin, new("Beard Oil", 30000, 4));

        var error = await Assert.ThrowsAsync<DomainError>(() => Catalogue.SetProductActive(Admin, "beard-oil", true));

        Assert.Equal("duplicate_name", error.Code);
        Assert.True(replacement.Active);
    }
}
