namespace Kinsmen.Web.Models.Api;

/// <summary>What /api/admin/dashboard returns. Counts are for bookings that start inside From..To (shop-local
/// dates, both included); low stock is current stock and ignores the dates.</summary>
public sealed class DashboardReport
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DashboardTotals Totals { get; set; } = new();
    public List<ServiceDemand> TopServices { get; set; } = [];
    public List<LowStockItem> LowStock { get; set; } = [];
}

public sealed class DashboardTotals
{
    public int Total { get; set; }
    public int Upcoming { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }
    public int NoShow { get; set; }
    public long RevenueCents { get; set; }
}

public sealed class ServiceDemand
{
    public string ServiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Bookings { get; set; }
}

public sealed class LowStockItem
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public int StockQuantity { get; set; }
    public int LowStockThreshold { get; set; }
}
