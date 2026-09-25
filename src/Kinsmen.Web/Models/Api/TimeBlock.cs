namespace Kinsmen.Web.Models.Api;

/// <summary>A barber's private time block (lunch, leave, etc.). Only visible to the
/// relevant barber/admin — never exposed through public availability.</summary>
public sealed class TimeBlock
{
    public string Id { get; set; } = string.Empty;
    public string BarberId { get; set; } = string.Empty;
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}
