using Kinsmen.Web.ApiClient;

namespace Kinsmen.Web.Helpers;

/// <summary>Maps a failed API call to a message that actually tells the person what
/// happened, instead of every failure looking like a generic connectivity problem
/// (the exact confusion a 401 caused during barber-schedule testing). Used by every
/// controller's catch blocks so the messaging is consistent across the whole app.</summary>
public static class ApiErrorMessages
{
    public static string For(KinsmenApiException ex) => ex.StatusCode switch
    {
        // Sign-in is Auth0 now (the old wording pointed at dev-token config that no
        // longer exists). A 401 means the API rejected the access token this session
        // carries - expired, or no longer valid - so logging in again is the fix.
        401 => "Your session has expired or is no longer valid - please log in again.",
        403 => "You don't have permission to do that with the current account.",
        404 => "That couldn't be found.",
        429 => "Too many requests - wait a moment and try again.",
        // What Render's proxy returns while the API is waking from its free-tier sleep. ColdStartRetryHandler
        // has already waited up to a minute for read-only calls, so this mostly reaches people when it's slow.
        502 or 503 or 504 => WakingUp,
        >= 500 => "The booking service hit a problem on its end. Try again shortly.",
        // 400/409/anything else: the API's own message is usually specific enough to
        // act on directly (e.g. exact validation problems, or the 409 conflict codes
        // BookingController/MyBookingsController already give bespoke UI treatment to).
        _ => ex.Message
    };

    // Locally the cause is usually that the API isn't running (docs/backend/GETTING-STARTED.md); on the live
    // site it's the API still waking up. The wording has to make sense to a customer, so it covers the latter.
    public static string ForConnectionFailure() =>
        "Couldn't reach the booking service. " + WakingUp;

    private const string WakingUp =
        "If the site hasn't been used for a while, the booking service takes up to a minute to start - please refresh shortly.";
}
