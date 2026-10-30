using System.Net;
using System.Net.Http.Json;
using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Tests;

/// <summary>Editing a product must never undo stock that bookings changed in the meantime, and product images
/// must be something the site's content security policy will actually let the browser show.</summary>
public sealed class ProductEditTests : IDisposable
{
    private readonly TestStore store = new();
    private readonly TestClock clock = new();
    private readonly ApiFactory factory = new();
    private CatalogueService Catalogue => new(store, clock, new());
    private BookingService Bookings => new(store, clock, new());
    private static readonly Actor Admin = new("admin-a", "Admin");
    private static readonly Actor Customer = new("customer-a", "Customer");

    public void Dispose() => factory.Dispose();

    private int StockOf(string id) => store.Inventory.Single(p => p.Id == id).StockQuantity;

    private static ProductRequest Edit(int? stock = null, string name = "Beard Oil", string? image = null)
        => new(name, 25000, stock, "Conditions beard hair.", "Beard Care", image, 5, 6);

    [Fact] public async Task EditingWithoutStockKeepsStockThatBookingsHaveChanged()
    {
        var before = StockOf("beard-oil");
        // A customer reserves two while an admin has the edit form open...
        await Bookings.Create(Customer, new("barber-a", ["haircut"], BookingTests.Start, Products: [new("beard-oil", 2)]));

        // ...then the admin saves a new description. The form didn't send a stock count, so none is changed.
        var updated = await Catalogue.UpdateProduct(Admin, "beard-oil", Edit(stock: null) with { Description = "New description." });

        Assert.Equal(before - 2, updated.StockQuantity);
        Assert.Equal(before - 2, StockOf("beard-oil"));
        Assert.Equal("New description.", store.Inventory.Single(p => p.Id == "beard-oil").Description);
    }

    [Fact] public async Task EditingWithAStockCountStillCorrectsTheCount()
    {
        var updated = await Catalogue.UpdateProduct(Admin, "beard-oil", Edit(stock: 40));

        Assert.Equal(40, updated.StockQuantity);
        Assert.Equal(40, StockOf("beard-oil"));
    }

    [Fact] public async Task AnOutOfRangeStockCountIsStillRejectedWhenGiven()
        => Assert.Equal(400, (await Assert.ThrowsAsync<DomainError>(() => Catalogue.UpdateProduct(Admin, "beard-oil", Edit(stock: -1)))).Status);

    [Fact] public async Task ANewProductMustSayHowMuchStockThereIs()
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateProduct(Admin, Edit(stock: null, name: "Pomade")));

        Assert.Equal(400, error.Status);
        Assert.DoesNotContain(store.Inventory, p => p.Name == "Pomade");
    }

    [Theory]
    [InlineData("https://images.example.com/cream.jpg")]
    [InlineData("https://cdn.example.com/a/b/c?v=3")]
    [InlineData("/img/products/beard-oil.jpg")]
    [InlineData("/img/products/Sea-Salts_2.WEBP")]
    public async Task ImagesTheBrowserCanShowAreAccepted(string image)
    {
        var product = await Catalogue.CreateProduct(Admin, Edit(stock: 5, name: "Pomade", image: image));

        Assert.Equal(image, product.ImageUrl);
    }

    [Theory]
    [InlineData("http://images.example.com/cream.jpg")]       // Mixed content: blocked on an https site.
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//evil.example.com/a.png")]                  // Protocol-relative: would load from another site.
    [InlineData("https://user:secret@images.example.com/a.jpg")]
    [InlineData("/img/other/a.png")]
    [InlineData("/img/products/../secret.png")]
    [InlineData("/img/products/folder/a.png")]
    [InlineData("/img/products/a.exe")]
    [InlineData("/img/products/.png")]
    [InlineData("beard-oil.jpg")]
    public async Task OtherImageAddressesAreRejected(string image)
    {
        var error = await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateProduct(Admin, Edit(stock: 5, name: "Pomade", image: image)));

        Assert.Equal(400, error.Status);
        Assert.Contains("img/products", error.Message);
    }

    [Fact] public async Task OverlyLongImageLinksAreRejected()
    {
        var image = "https://images.example.com/" + new string('a', 2100) + ".jpg";

        await Assert.ThrowsAsync<DomainError>(() => Catalogue.CreateProduct(Admin, Edit(stock: 5, name: "Pomade", image: image)));
    }

    [Fact] public async Task TheEndpointAcceptsAnEditThatLeavesStockOut()
    {
        var before = StockOfInFactory("beard-oil");
        var body = new { name = "Beard Oil", priceCents = 26000, description = "Softer.", category = "Beard Care", lowStockThreshold = 5, displayOrder = 6 };

        var response = await factory.Client("admin-a", "Admin").PutAsJsonAsync("/api/admin/products/beard-oil", body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(before, StockOfInFactory("beard-oil"));
        Assert.Equal(26000, FactoryStore.Inventory.Single(p => p.Id == "beard-oil").PriceCents); // The rest of the edit was applied.
    }

    private TestStore FactoryStore => (TestStore)factory.Services.GetService(typeof(IBookingStore))!;
    private int StockOfInFactory(string id) => FactoryStore.Inventory.Single(p => p.Id == id).StockQuantity;

    [Fact] public async Task TheEndpointRejectsANewProductWithoutStock()
    {
        var body = new { name = "Pomade", priceCents = 20000 };

        var response = await factory.Client("admin-a", "Admin").PostAsJsonAsync("/api/admin/products", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
