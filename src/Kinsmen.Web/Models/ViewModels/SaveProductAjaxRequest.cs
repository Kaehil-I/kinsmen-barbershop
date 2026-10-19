namespace Kinsmen.Web.Models.ViewModels;

public sealed class SaveProductAjaxRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceCents { get; set; }
    public int StockQuantity { get; set; }
}
