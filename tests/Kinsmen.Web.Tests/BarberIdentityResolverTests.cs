using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;
using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.Tests;

public sealed class BarberIdentityResolverTests
{
    private static readonly DateTimeOffset From = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddDays(1);

    private static List<Barber> Barbers(params string[] ids)
        => ids.Select(id => new Barber { Id = id, Name = "Name of " + id }).ToList();

    private static Task<List<TimeBlock>> Allowed() => Task.FromResult(new List<TimeBlock>());

    private static Task<List<TimeBlock>> Refused(int status)
        => throw new KinsmenApiException(status, null, "refused");

    // The API answers 200 for the barber linked to the caller's login and 403 for every
    // other barber - so the one that answers is "me".
    [Fact]
    public async Task ReturnsTheBarberWhoseBlocksTheCallerMayRead()
    {
        var client = new BlocksOnlyClient(id => id == "barber-b" ? Allowed() : Refused(403));

        var found = await BarberIdentityResolver.FindOwnBarberAsync(
            client, Barbers("barber-a", "barber-b", "barber-c"), From, To, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("barber-b", found.Id);
    }

    [Fact]
    public async Task ReturnsNullWhenNoBarberIsLinkedToTheCaller()
    {
        var client = new BlocksOnlyClient(_ => Refused(403));

        var found = await BarberIdentityResolver.FindOwnBarberAsync(
            client, Barbers("barber-a", "barber-b"), From, To, CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task ANotFoundBarberIsTreatedTheSameAsAForbiddenOne()
    {
        var client = new BlocksOnlyClient(id => id == "barber-b" ? Allowed() : Refused(404));

        var found = await BarberIdentityResolver.FindOwnBarberAsync(
            client, Barbers("barber-a", "barber-b"), From, To, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("barber-b", found.Id);
    }

    // Only "not yours" (403) and "gone" (404) mean keep looking. A server error or an
    // outage must surface, not quietly turn into "you have no barber profile".
    [Fact]
    public async Task OtherFailuresAreNotSwallowed()
    {
        var client = new BlocksOnlyClient(_ => Refused(500));

        await Assert.ThrowsAsync<KinsmenApiException>(() => BarberIdentityResolver.FindOwnBarberAsync(
            client, Barbers("barber-a"), From, To, CancellationToken.None));
    }

    [Fact]
    public async Task NoCandidatesMeansNoMatch()
    {
        var client = new BlocksOnlyClient(_ => Allowed());

        var found = await BarberIdentityResolver.FindOwnBarberAsync(
            client, new List<Barber>(), From, To, CancellationToken.None);

        Assert.Null(found);
    }

    // Only GetBarberBlocksAsync does anything; every other member of the interface is
    // deliberately unimplemented so a test that accidentally reaches one fails loudly.
    private sealed class BlocksOnlyClient(Func<string, Task<List<TimeBlock>>> onBlocks) : IKinsmenApiClient
    {
        public Task<List<TimeBlock>> GetBarberBlocksAsync(
            string barberId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
            => onBlocks(barberId);

        public Task<List<Service>> GetServicesAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<Barber>> GetBarbersAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<AvailableSlot>> GetAvailabilityAsync(
            DateOnly date, IReadOnlyCollection<string> serviceIds, string? barberId = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Booking> CreateBookingAsync(
            CreateBookingRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<Booking>> GetBookingsAsync(
            DateTimeOffset from, DateTimeOffset to, string? barberId = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Booking> GetBookingAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Booking> RescheduleBookingAsync(
            string id, RescheduleRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Booking> CancelBookingAsync(
            string id, VersionRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Booking> UpdateBookingStatusAsync(
            string id, StatusChangeRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<TimeBlock> CreateBarberBlockAsync(
            string barberId, CreateBlockRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task DeleteBarberBlockAsync(string blockId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<Service>> GetAdminServicesAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Service> CreateAdminServiceAsync(ServiceInput input, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Service> UpdateAdminServiceAsync(
            string id, ServiceInput input, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<Service> SetAdminServiceActiveAsync(
            string id, bool active, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<AdminBarber>> GetAdminBarbersAsync(CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<AdminBarber> CreateAdminBarberAsync(BarberInput input, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<AdminBarber> UpdateAdminBarberAsync(
            string id, BarberInput input, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<AdminBarber> SetAdminBarberActiveAsync(
            string id, bool active, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
