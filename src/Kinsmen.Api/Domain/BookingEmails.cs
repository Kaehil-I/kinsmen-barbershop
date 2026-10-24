using System.Globalization;
using System.Net;
using System.Text;

namespace Kinsmen.Api.Domain;

public sealed record EmailMessage(string Subject, string Html);
public sealed record ReservedProduct(string Name, int Quantity = 1);

/// <summary>Builds every customer-facing booking email. Pure functions: no I/O, so they are easy to test.
/// Everything that comes from data is HTML-encoded, and times are shown in the shop's timezone.</summary>
public static class BookingEmails
{
    private static readonly TimeZoneInfo ShopZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const string ChangeHint = "Need to change something? Sign in and open My Bookings.";

    public static EmailMessage Received(Booking b, string barberName, IReadOnlyList<ReservedProduct>? products = null)
        => new("We've received your booking request",
            Layout("Booking request received",
                "Thanks for booking with Kinsmen! We've received your request and will email you again once it's confirmed.",
                b, barberName, products, ChangeHint));

    public static EmailMessage Confirmed(Booking b, string barberName, IReadOnlyList<ReservedProduct>? products = null)
        => new("Your booking is confirmed",
            Layout("Your booking is confirmed", "Your appointment is confirmed. See you then!",
                b, barberName, products, ChangeHint));

    public static EmailMessage Cancelled(Booking b, string barberName, IReadOnlyList<ReservedProduct>? products = null)
        => new("Your booking has been cancelled",
            Layout("Booking cancelled", "Your booking has been cancelled. The details below are for your records.",
                b, barberName, products, "We'd love to see you another time. You can book again whenever suits you."));

    public static EmailMessage Rescheduled(Booking b, DateTime previousStartUtc, string barberName, IReadOnlyList<ReservedProduct>? products = null)
        => new("Your booking has been rescheduled",
            Layout("Booking rescheduled",
                $"Your appointment has moved. It was previously {When(previousStartUtc)}; the new details are below.",
                b, barberName, products, ChangeHint));

    public static EmailMessage Reminder(Booking b, string barberName, IReadOnlyList<ReservedProduct>? products = null)
        => new($"Reminder: your appointment on {Day(b.StartUtc)}",
            Layout("Appointment reminder", "Just a reminder that your appointment is coming up.",
                b, barberName, products, ChangeHint));

    public static EmailMessage ReviewRequest()
        => new("How was your visit?", Wrap(
            "<h2 style=\"margin:0 0 12px\">How was your visit?</h2>" +
            "<p>Thanks for visiting Kinsmen! We'd love to hear how it went. Sign in and open My Bookings to leave a review.</p>"));

    // ---- helpers ----
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static DateTime Local(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ShopZone);
    private static string Day(DateTime utc) => Local(utc).ToString("dddd d MMMM", Inv);
    private static string When(DateTime utc) => $"{Day(utc)} at {Local(utc).ToString("HH:mm", Inv)}";
    private static string Money(int cents) => "R" + (cents / 100m).ToString("0.00", Inv);

    private static string Wrap(string inner)
        => "<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:520px;margin:0 auto;color:#222;line-height:1.5\">"
           + inner + "<p style=\"color:#777;font-size:12px;margin-top:24px\">Kinsmen Barbershop</p></div>";

    private static void Row(StringBuilder sb, string label, string htmlValue)
        => sb.Append("<tr><td style=\"padding:6px 12px 6px 0;color:#777;vertical-align:top;white-space:nowrap\">")
             .Append(label).Append("</td><td style=\"padding:6px 0\">").Append(htmlValue).Append("</td></tr>");

    private static string Layout(string heading, string intro, Booking b, string barberName,
        IReadOnlyList<ReservedProduct>? products, string footer)
    {
        var sb = new StringBuilder();
        sb.Append("<h2 style=\"margin:0 0 12px\">").Append(E(heading)).Append("</h2><p>").Append(E(intro)).Append("</p>");
        sb.Append("<table style=\"width:100%;border-collapse:collapse;border-top:1px solid #ddd;border-bottom:1px solid #ddd\">");
        Row(sb, "Service", string.Join("<br>", b.Services.Select(s => $"{E(s.Name)} ({Money(s.PriceCents)})")));
        Row(sb, "Barber", E(barberName));
        Row(sb, "When", E(When(b.StartUtc)));
        Row(sb, "Duration", $"{(int)(b.EndUtc - b.StartUtc).TotalMinutes} minutes");
        if (products is { Count: > 0 })
            Row(sb, "Reserved products", string.Join("<br>", products.Select(p => $"{E(p.Name)} &times; {p.Quantity}")));
        Row(sb, "Total", Money(b.TotalCents));
        Row(sb, "Reference", E(b.Id.Length > 8 ? b.Id[..8].ToUpperInvariant() : b.Id.ToUpperInvariant()));
        sb.Append("</table><p>").Append(E(footer)).Append("</p>");
        return Wrap(sb.ToString());
    }
}