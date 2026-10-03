using System.Security.Claims;

namespace Kinsmen.Web.Helpers;

public static class ClaimsPrincipalExtensions
{
    /// <summary>Barbers and admins. Staff don't book appointments, so the customer booking
    /// screens (and the buttons that lead to them) are kept away from them. One definition
    /// here so the nav, the views and the controllers can't drift apart on who counts.</summary>
    public static bool IsStaff(this ClaimsPrincipal user)
        => user.IsInRole("Barber") || user.IsInRole("Admin");
}
