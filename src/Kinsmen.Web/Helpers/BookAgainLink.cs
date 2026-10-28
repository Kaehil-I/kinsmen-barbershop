using Kinsmen.Web.Models.Api;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Helpers;

/// <summary>"Book again": the booking page with a past visit's barber and services already picked.
/// Shared by My Bookings and the Profile page so both build the link the same way.</summary>
public static class BookAgainLink
{
    public static string For(IUrlHelper url, Booking booking)
    {
        var query = new List<string> { "barberId=" + Uri.EscapeDataString(booking.BarberId) };
        query.AddRange(booking.Services.Select(s => "serviceIds=" + Uri.EscapeDataString(s.ServiceId)));
        return url.Action("Index", "Booking") + "?" + string.Join("&", query);
    }
}
