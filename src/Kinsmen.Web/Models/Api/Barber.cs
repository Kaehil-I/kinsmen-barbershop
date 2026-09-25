namespace Kinsmen.Web.Models.Api;

/// <summary>One weekly working period for a barber. Weekday is 1-7 (ISO, Monday = 1);
/// startMinute/endMinute are minutes since local midnight.</summary>
public sealed class WorkingPeriod
{
    public int Weekday { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

/// <summary>A public barber profile (GET /api/barbers). Staff account IDs are omitted —
/// this is not the same as the account Barber role.</summary>
public sealed class Barber
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<WorkingPeriod> Hours { get; set; } = [];
}
