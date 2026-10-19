namespace Kinsmen.Api.Domain;

/// <summary>
/// Retail products currently sold at the barbershop. This is a browse-only catalogue:
/// purchases remain in store until payment and fulfilment are designed. Stock figures
/// are illustrative until the business supplies a stock count.
/// </summary>
public sealed record RetailProduct(string Id, string Name, int PriceCents, int StockQuantity);

public static class ProductCatalogue
{
    public static readonly RetailProduct[] Items =
    [
        new("sea-salts", "Sea Salts", 40000, 18),
        new("texture-powder", "Texture Powder", 40000, 24),
        new("ruck-clay-wax", "Lock Stock & Barrel Ruck Clay / Wax", 45000, 11),
        new("pucka-clay-wax", "Lock Stock & Barrel Pucka Clay / Wax", 45000, 16),
        new("matify", "Matify", 45000, 9),
        new("beard-oil", "Beard Oil", 25000, 21)
    ];
}
