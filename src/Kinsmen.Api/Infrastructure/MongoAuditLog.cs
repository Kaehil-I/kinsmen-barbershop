using Kinsmen.Api.Domain;
using MongoDB.Driver;

namespace Kinsmen.Api.Infrastructure;

// Append-only record of staff role changes. Written outside a transaction (it records a change already made
// in Auth0), so MongoDB creates the collection on first use; existing databases need no re-initialisation.
public sealed class MongoAuditLog(MongoBookingStore store) : IAuditLog
{
    private IMongoCollection<AuditEntry> Entries => store.Database.GetCollection<AuditEntry>("auditLog");

    public Task Record(AuditEntry entry, CancellationToken ct) => Entries.InsertOneAsync(entry, cancellationToken: ct);

    public async Task<List<AuditEntry>> Recent(int count, CancellationToken ct)
        => await Entries.Find(Builders<AuditEntry>.Filter.Empty).SortByDescending(e => e.AtUtc).Limit(count).ToListAsync(ct);
}
