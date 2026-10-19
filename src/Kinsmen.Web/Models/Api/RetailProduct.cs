namespace Kinsmen.Web.Models.Api;

/// <summary>One product displayed in the public in-store retail catalogue.</summary>
public sealed class RetailProduct
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int StockQuantity { get; set; }
    public bool Active { get; set; }
}
