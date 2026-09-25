namespace Kinsmen.Web.Helpers;

/// <summary>Formats the API's integer ZAR-cents amounts for display. The API never
/// hands the client a decimal price — this is purely a display-layer conversion.</summary>
public static class PriceFormatter
{
    /// <summary>"R200" for a whole-rand amount, "R199.99" otherwise — matches the
    /// prototype's plain "R{price}" style for the common whole-rand case.</summary>
    public static string FormatZarCents(int cents)
    {
        var rands = cents / 100m;
        return rands == Math.Floor(rands) ? $"R{rands:0}" : $"R{rands:0.00}";
    }
}
