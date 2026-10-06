using System.Globalization;

namespace Kinsmen.Web.Helpers;

/// <summary>Formats the API's integer ZAR-cents amounts for display. The API never
/// hands the client a decimal price - this is purely a display-layer conversion.</summary>
public static class PriceFormatter
{
    /// <summary>"R200" for a whole-rand amount, "R199.99" otherwise - matches the
    /// prototype's plain "R{price}" style for the common whole-rand case.</summary>
    public static string FormatZarCents(int cents)
    {
        var rands = cents / 100m;

        // InvariantCulture on purpose: on a South African machine the current culture
        // uses a comma decimal separator ("R199,99"), while the booking page's JS
        // (formatZarCents in booking.js) always uses a dot - so server-rendered and
        // JS-rendered prices on the same site would disagree.
        var text = rands == Math.Floor(rands)
            ? rands.ToString("0", CultureInfo.InvariantCulture)
            : rands.ToString("0.00", CultureInfo.InvariantCulture);

        return "R" + text;
    }
}
