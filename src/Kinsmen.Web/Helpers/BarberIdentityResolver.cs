using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Helpers;

/// <summary>Works out which barber profile the signed-in barber owns.
///
/// The API has no "who am I" endpoint, but it enforces the answer anyway: listing a
/// barber's time blocks succeeds only for the barber whose linked login matches the
/// caller (BookingService.StaffAccess) and returns 403 for every other barber. So
/// probing each active barber, the one that answers 200 is the caller's own profile.
///
/// This is an interim measure - a one-line GET /api/barbers/me on the API would replace
/// it (and this class) outright. It must NOT be used for admins: an admin is allowed to
/// read every barber's blocks, so every probe would succeed and prove nothing.</summary>
public static class BarberIdentityResolver
{
    public static async Task<Barber?> FindOwnBarberAsync(
        IKinsmenApiClient apiClient, IEnumerable<Barber> candidates,
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        async Task<Barber?> Probe(Barber barber)
        {
            try
            {
                await apiClient.GetBarberBlocksAsync(barber.Id, from, to, cancellationToken);
                return barber;
            }
            catch (KinsmenApiException ex) when (ex.StatusCode is 403 or 404)
            {
                // Not this caller's profile (or it no longer exists) - keep looking.
                return null;
            }
        }

        var results = await Task.WhenAll(candidates.Select(barber => Probe(barber)));
        return results.FirstOrDefault(barber => barber is not null);
    }
}
