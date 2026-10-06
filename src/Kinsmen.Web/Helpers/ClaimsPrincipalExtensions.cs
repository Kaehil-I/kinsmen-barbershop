using System.Security.Claims;

namespace Kinsmen.Web.Helpers;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Barbers and admins. Staff don't book appointments, so the customer booking
    /// screens (and the buttons that lead to them) are kept away from them. One definition
    /// here so the nav, the views and the controllers can't drift apart on who counts.</summary>
    public static bool IsStaff(this ClaimsPrincipal user)
        => user.IsInRole("Barber") || user.IsInRole("Admin");

    /// <summary>
    /// Returns a presentation-safe name for the navigation. Auth0's <c>sub</c> claim
    /// is a stable internal identifier, not something a customer should see. Prefer
    /// profile claims and use a neutral label when the sign-in has no profile data.
    /// </summary>
    public static string DisplayName(this ClaimsPrincipal user)
    {
        var displayName = user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("nickname")?.Value;

        if (!string.IsNullOrWhiteSpace(displayName) && !LooksLikeAuth0Subject(displayName))
            return displayName;

        var email = user.FindFirst(ClaimTypes.Email)?.Value ?? user.FindFirst("email")?.Value;
        if (!string.IsNullOrWhiteSpace(email))
            return email;

        return "Signed-in user";
    }

    private static bool LooksLikeAuth0Subject(string value)
        => value.Contains('|') || value.StartsWith("auth0", StringComparison.OrdinalIgnoreCase);
}
