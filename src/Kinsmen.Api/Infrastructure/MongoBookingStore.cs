using Kinsmen.Api.Domain;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace Kinsmen.Api.Infrastructure;

public sealed class MongoBookingStore : IBookingStore
{
    private readonly IMongoClient client;
    private readonly IMongoDatabase db;
    static MongoBookingStore()
    {
        ConventionRegistry.Register("kinsmen", new ConventionPack
        {
            new CamelCaseElementNameConvention(), new EnumRepresentationConvention(BsonType.String)
        }, t => t.Namespace == "Kinsmen.Api.Domain");
    }
    public MongoBookingStore(string connection, string database)
    {
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        client = new MongoClient(settings);
        db = client.GetDatabase(database);
    }
    public Task<T> Read<T>(Func<IBookingSession, Task<T>> action, CancellationToken ct = default)
        => action(new MongoSession(db, null, ct));

    public async Task<T> Write<T>(Func<IBookingSession, Task<T>> action, CancellationToken ct = default)
    {
        using var session = await client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync((s, token) => action(new MongoSession(db, s, token)),
            new TransactionOptions(ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority), ct);
    }

    public async Task Initialize(CancellationToken ct = default)
    {
        // Pre-create collections outside transactions, with server-side JSON-schema validation.
        foreach (var name in new[] { "services", "barbers", "bookings", "timeBlocks" })
        {
            var names = await (await db.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
            if (!names.Contains(name))
            {
                var schema = Schemas.For(name);
                try
                {
                    await db.CreateCollectionAsync(name, new CreateCollectionOptions<BsonDocument>
                    {
                        Validator = new BsonDocument("$jsonSchema", BsonDocument.Parse(schema)),
                        ValidationAction = DocumentValidationAction.Error, ValidationLevel = DocumentValidationLevel.Strict
                    }, ct);
                }
                catch (MongoCommandException e) when (e.Code == 48) { /* Another initializer created it. */ }
            }
            // Explicit initialization also updates validation on pre-existing collections.
            // This does not migrate existing data; review data compatibility before production changes.
            await db.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                { "collMod", name }, { "validator", new BsonDocument("$jsonSchema", BsonDocument.Parse(Schemas.For(name))) },
                { "validationLevel", "strict" }, { "validationAction", "error" }
            }, cancellationToken: ct);
        }
        var bookings = db.GetCollection<Booking>("bookings");
        await bookings.Indexes.CreateManyAsync([
            new CreateIndexModel<Booking>(Builders<Booking>.IndexKeys.Ascending(x => x.BarberId).Ascending(x => x.StartUtc).Ascending(x => x.EndUtc)),
            new CreateIndexModel<Booking>(Builders<Booking>.IndexKeys.Ascending(x => x.CustomerId).Ascending(x => x.StartUtc))
        ], ct);
        await db.GetCollection<TimeBlock>("timeBlocks").Indexes.CreateOneAsync(new CreateIndexModel<TimeBlock>(
            Builders<TimeBlock>.IndexKeys.Ascending(x => x.BarberId).Ascending(x => x.StartUtc).Ascending(x => x.EndUtc)), cancellationToken: ct);
        await db.GetCollection<Barber>("barbers").Indexes.CreateOneAsync(new CreateIndexModel<Barber>(
            Builders<Barber>.IndexKeys.Ascending(x => x.UserId), new CreateIndexOptions { Unique = true }), cancellationToken: ct);
    }

    public async Task SeedDemo(CancellationToken ct = default)
    {
        // Synthetic examples only. SetOnInsert preserves subsequent edits and makes reruns safe.
        foreach (var service in DemoData.Services)
        {
            var update = Builders<ServiceItem>.Update.SetOnInsert(x => x.Name, service.Name)
                .SetOnInsert(x => x.PriceCents, service.PriceCents).SetOnInsert(x => x.DurationMinutes, service.DurationMinutes)
                .SetOnInsert(x => x.Active, true);
            await db.GetCollection<ServiceItem>("services").UpdateOneAsync(x => x.Id == service.Id, update, new UpdateOptions { IsUpsert = true }, ct);
        }
        foreach (var barber in DemoData.Barbers)
        {
            var update = Builders<Barber>.Update.SetOnInsert(x => x.Name, barber.Name).SetOnInsert(x => x.UserId, barber.UserId)
                .SetOnInsert(x => x.Hours, barber.Hours).SetOnInsert(x => x.Active, true).SetOnInsert(x => x.Revision, 0);
            await db.GetCollection<Barber>("barbers").UpdateOneAsync(x => x.Id == barber.Id, update, new UpdateOptions { IsUpsert = true }, ct);
        }
    }
    public async Task Ping(CancellationToken ct)
    {
        await db.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: ct);
        var hello = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("hello", 1), cancellationToken: ct);
        if (!hello.Contains("setName") && hello.GetValue("msg", "").AsString != "isdbgrid")
            throw new DomainError(503, "database_not_ready", "MongoDB must support transactions (replica set or sharded cluster).");
        var names = await (await db.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct);
        if (new[] { "services", "barbers", "bookings", "timeBlocks" }.Any(n => !names.Contains(n)))
            throw new DomainError(503, "database_not_ready", "Initialize the application database before accepting bookings.");
    }
}

internal sealed class MongoSession(IMongoDatabase db, IClientSessionHandle? session, CancellationToken ct) : IBookingSession
{
    private async Task<List<T>> Find<T>(string name, FilterDefinition<T> filter)
    {
        var c = db.GetCollection<T>(name);
        using var cursor = session is null ? await c.FindAsync(filter, cancellationToken: ct)
            : await c.FindAsync(session, filter, cancellationToken: ct);
        return await cursor.ToListAsync(ct);
    }
    private IClientSessionHandle WriteSession => session ?? throw new InvalidOperationException("Writes require a transaction.");
    public Task<List<ServiceItem>> Services() => Find("services", Builders<ServiceItem>.Filter.Empty);
    public Task<List<Barber>> Barbers() => Find("barbers", Builders<Barber>.Filter.Empty);
    public async Task TouchBarbers(IEnumerable<string> ids)
    {
        foreach (var id in ids.Distinct().Order(StringComparer.Ordinal))
        {
            var result = await db.GetCollection<Barber>("barbers").UpdateOneAsync(WriteSession, b => b.Id == id,
                Builders<Barber>.Update.Inc(b => b.Revision, 1), cancellationToken: ct);
            if (result.MatchedCount != 1) throw DomainError.Missing();
        }
    }
    public async Task<Booking?> BookingById(string id) => (await Find("bookings", Builders<Booking>.Filter.Eq(x => x.Id, id))).SingleOrDefault();
    public Task<List<Booking>> Bookings(string? customerId, string? barberId, DateTime from, DateTime to)
    {
        var f = Builders<Booking>.Filter;
        var filter = f.Lt(x => x.StartUtc, to) & f.Gt(x => x.EndUtc, from);
        if (customerId is not null) filter &= f.Eq(x => x.CustomerId, customerId);
        if (barberId is not null) filter &= f.Eq(x => x.BarberId, barberId);
        return Find("bookings", filter);
    }
    public Task<List<TimeBlock>> Blocks(string barberId, DateTime from, DateTime to)
    {
        var f = Builders<TimeBlock>.Filter;
        return Find("timeBlocks", f.Eq(x => x.BarberId, barberId) & f.Lt(x => x.StartUtc, to) & f.Gt(x => x.EndUtc, from));
    }
    public async Task<TimeBlock?> BlockById(string id) => (await Find("timeBlocks", Builders<TimeBlock>.Filter.Eq(x => x.Id, id))).SingleOrDefault();
    public async Task SaveBooking(Booking booking, bool insert = false)
    {
        var collection = db.GetCollection<Booking>("bookings");
        if (insert)
        {
            try { await collection.InsertOneAsync(WriteSession, booking, cancellationToken: ct); }
            catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
            { throw new DuplicateBookingIdException(); }
        }
        else
        {
            var result = await collection.ReplaceOneAsync(WriteSession, x => x.Id == booking.Id && x.Version == booking.Version - 1,
                booking, cancellationToken: ct);
            if (result.MatchedCount != 1) throw new DomainError(409, "stale_version", "Refresh this booking before changing it.");
        }
    }
    public Task SaveBlock(TimeBlock block) => db.GetCollection<TimeBlock>("timeBlocks").InsertOneAsync(WriteSession, block, cancellationToken: ct);
    public async Task DeleteBlock(string id) => await db.GetCollection<TimeBlock>("timeBlocks").DeleteOneAsync(WriteSession, x => x.Id == id, cancellationToken: ct);
    public async Task SaveService(ServiceItem service, bool insert = false)
    {
        var collection = db.GetCollection<ServiceItem>("services");
        if (insert) { await collection.InsertOneAsync(WriteSession, service, cancellationToken: ct); return; }
        var result = await collection.ReplaceOneAsync(WriteSession, x => x.Id == service.Id, service, cancellationToken: ct);
        if (result.MatchedCount != 1) throw DomainError.Missing();
    }
    public async Task SaveBarber(Barber barber, bool insert = false)
    {
        var collection = db.GetCollection<Barber>("barbers");
        try
        {
            if (insert) { await collection.InsertOneAsync(WriteSession, barber with { Revision = 0 }, cancellationToken: ct); return; }
            // Update profile fields only; the revision is incremented, never overwritten, to keep schedule locking intact.
            var result = await collection.UpdateOneAsync(WriteSession, x => x.Id == barber.Id, Builders<Barber>.Update
                .Set(x => x.Name, barber.Name).Set(x => x.UserId, barber.UserId).Set(x => x.Hours, barber.Hours)
                .Set(x => x.Active, barber.Active).Inc(x => x.Revision, 1), cancellationToken: ct);
            if (result.MatchedCount != 1) throw DomainError.Missing();
        }
        catch (MongoWriteException e) when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        { throw new DuplicateBarberUserException(); }
    }
}

public static class DemoData
{
    public static readonly ServiceItem[] Services = [new("haircut", "Demo haircut", 20000, 30), new("beard", "Demo beard trim", 10000, 15)];
    public static readonly Barber[] Barbers = [
        new("barber-a", "Demo barber A", "staff-a", Enumerable.Range(1, 6).Select(d => new WorkingPeriod(d, 540, 1020)).ToArray()),
        new("barber-b", "Demo barber B", "staff-b", Enumerable.Range(1, 6).Select(d => new WorkingPeriod(d, 540, 1020)).ToArray())
    ];
}
