namespace Kinsmen.Web.Models.Api;

/// <summary>A bookable service from the public catalogue (GET /api/services).</summary>
public sealed class Service
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Price in integer ZAR cents — never a calculated/display decimal from the client.</summary>
    public int PriceCents { get; set; }

    public int DurationMinutes { get; set; }
    public bool Active { get; set; }
}
