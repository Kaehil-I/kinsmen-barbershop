namespace Kinsmen.Web.Helpers;

/// <summary>Named rate-limit policies (configured in Program.cs) for [EnableRateLimiting(...)].
/// Every request also counts towards the general per-person limit.</summary>
public static class RateLimitPolicies
{
    /// <summary>Anything that changes data: bookings, cancellations, status changes, time blocks, admin edits.</summary>
    public const string Writes = "writes";

    /// <summary>Starting an Auth0 sign-in or sign-up.</summary>
    public const string Login = "login";
}
