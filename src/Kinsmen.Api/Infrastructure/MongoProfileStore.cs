using Kinsmen.Api.Domain;
using MongoDB.Driver;

namespace Kinsmen.Api.Infrastructure;

// One document per customer, keyed by their user id. Like the audit log it is written outside a transaction,
// so MongoDB creates the collection on first use and existing databases need no re-initialisation.
public sealed class MongoProfileStore(MongoBookingStore store) : IProfileStore
{
    private IMongoCollection<CustomerProfile> Profiles => store.Database.GetCollection<CustomerProfile>("customerProfiles");

    public async Task<CustomerProfile?> Get(string userId, CancellationToken ct)
        => await Profiles.Find(p => p.Id == userId).FirstOrDefaultAsync(ct);

    public Task Save(CustomerProfile profile, CancellationToken ct)
        => Profiles.ReplaceOneAsync(p => p.Id == profile.Id, profile, new ReplaceOptions { IsUpsert = true }, ct);
}
