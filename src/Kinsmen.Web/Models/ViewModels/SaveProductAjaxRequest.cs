namespace Kinsmen.Web.Models.ViewModels;

public sealed class SaveProductAjaxRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int StockQuantity { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public string? ImageUrl { get; set; }
    public int LowStockThreshold { get; set; } = 5;
    public int DisplayOrder { get; set; }
}
