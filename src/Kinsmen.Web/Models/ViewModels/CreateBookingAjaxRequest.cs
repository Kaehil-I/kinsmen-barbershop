namespace Kinsmen.Web.Models.ViewModels;

/// <summary>Body the booking page's JS posts to BookingController.Create. Distinct from
/// Models.Api.CreateBookingRequest because StartUtc arrives as a plain ISO string picked
/// straight off an availability slot the JS already has, rather than something the
/// browser needs to construct a DateTimeOffset for.</summary>
public sealed class CreateBookingAjaxRequest
{
    /// <summary>Null/empty means "any available barber" — matches what the customer
    /// picked in the barber dropdown.</summary>
    public string? BarberId { get; set; }

    public List<string> ServiceIds { get; set; } = [];

    /// <summary>The exact startUtc value from the availability slot the customer clicked.</summary>
    public string StartUtc { get; set; } = string.Empty;

    /// <summary>Optional note for the barber, up to 500 chars. Rendered as plain text
    /// wherever it's shown - never as raw HTML.</summary>
    public string? Notes { get; set; }
}
