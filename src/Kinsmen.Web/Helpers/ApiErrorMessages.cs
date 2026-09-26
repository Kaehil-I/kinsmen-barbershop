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
        // Dev-token specific wording, since that's the actual cause every time this
        // has come up in this project so far - there's no real login yet.
        401 => "Your access token wasn't accepted - it may have expired (dev tokens " +
               "last 30 minutes) or was generated against a different signing key than " +
               "the one the API is currently running with. Generate a fresh token and " +
               "update DevTokens in appsettings.Development.json.",
        403 => "You don't have permission to do that with the current account.",
        404 => "That couldn't be found.",
        429 => "Too many requests - wait a moment and try again.",
        >= 500 => "The booking service hit a problem on its end. Try again shortly.",
        // 400/409/anything else: the API's own message is usually specific enough to
        // act on directly (e.g. exact validation problems, or the 409 conflict codes
        // BookingController/MyBookingsController already give bespoke UI treatment to).
        _ => ex.Message
    };

    public static string ForConnectionFailure() =>
        "Couldn't reach the booking service - make sure the API is running (see docs/backend/GETTING-STARTED.md) and refresh.";
}
