using Kinsmen.Api.Domain;
using Kinsmen.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Kinsmen.Api.Tests;

public sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KINSMEN_TEST_MONGO")))
            Skip = "Set KINSMEN_TEST_MONGO to a local/test replica set to run real MongoDB tests.";
    }
}

public sealed class MongoTests : IAsyncLifetime
{
    private readonly string connection = Environment.GetEnvironmentVariable("KINSMEN_TEST_MONGO") ?? "mongodb://127.0.0.1:27017/?replicaSet=rs0";
    private readonly string database = "kinsmen_test_" + Guid.NewGuid().ToString("N");
    private MongoBookingStore store = null!;
    private BookingService Service => new(store, new TestClock(), new());
    private static readonly Actor Customer = new("customer-a", "Customer");
    private static CreateBookingRequest Request => new("barber-a", ["haircut", "beard"], BookingTests.Start);
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("KINSMEN_TEST_MONGO") is null) return;
        store = new(connection, database); await store.Initialize(); await store.SeedDemo();
    }
    public async Task DisposeAsync()
    {
        if (store is not null) await new MongoClient(connection).DropDatabaseAsync(database);
    }
    [MongoFact] public async Task IndependentStoreInstancesCannotDoubleBook()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var service = new BookingService(new MongoBookingStore(connection, database), new TestClock(), new());
            try { await service.Create(Customer, Request); return 201; }
            catch (DomainError e) { return e.Status; }
        }));
        Assert.Equal(1, outcomes.Count(x => x == 201)); Assert.Equal(9, outcomes.Count(x => x == 409));
    }
    [MongoFact] public async Task BlockAndBookingRaceCannotBothSucceed()
    {
        async Task<int> Book() { try { await Service.Create(Customer, Request); return 201; } catch (DomainError e) { return e.Status; } }
        async Task<int> Block()
        {
            try { await Service.AddBlock(new("staff-a", "Barber"), "barber-a", new(BookingTests.Start, BookingTests.Start.AddHours(1), "Break")); return 201; }
            catch (DomainError e) { return e.Status; }
        }
        var outcomes = await Task.WhenAll(Book(), Block());
        Assert.Single(outcomes, x => x == 201); Assert.Single(outcomes, x => x == 409);
    }
    [MongoFact] public async Task FailedMoveRollsBackAndNewConnectionReadsOriginal()
    {
        var b = await Service.Create(Customer, Request);
        await Service.Create(Customer, Request with { Start = BookingTests.Start.AddHours(1) });
        await Assert.ThrowsAsync<DomainError>(() => Service.Reschedule(Customer, b.Id, new(BookingTests.Start.AddHours(1), 1)));
        var fresh = new MongoBookingStore(connection, database);
        var persisted = await fresh.Read(s => s.BookingById(b.Id));
        Assert.Equal(b.StartUtc, persisted!.StartUtc); Assert.Equal(1, persisted.Version); Assert.Equal(30000, persisted.TotalCents);
    }
    [MongoFact] public async Task CancellationReleasesSlotAndSeedIsRepeatable()
    {
        await store.SeedDemo(); Assert.Equal(2, (await Service.Services(default)).Count);
        var b = await Service.Create(Customer, Request); await Service.Cancel(Customer, b.Id, 1);
        await Service.Create(Customer, Request);
    }
    [MongoFact] public async Task DatabaseSchemaRejectsNegativePriceEvenOutsideApplication()
    {
        var db = new MongoClient(connection).GetDatabase(database);
        var ex = await Assert.ThrowsAsync<MongoWriteException>(() => db.GetCollection<BsonDocument>("services")
            .InsertOneAsync(new BsonDocument { { "_id", "bad" }, { "name", "Bad" }, { "priceCents", -1 }, { "durationMinutes", 30 }, { "active", true } }));
        Assert.Equal(121, ex.WriteError.Code);
    }
    [MongoFact] public async Task InitializationCanBeRepeatedAndReadinessRequiresCollections()
    {
        await store.Initialize(); await store.Ping(default);
        var empty = new MongoBookingStore(connection, database + "_empty");
        Assert.Equal(503, (await Assert.ThrowsAsync<DomainError>(() => empty.Ping(default))).Status);
    }
    [MongoFact] public async Task TenSimultaneousRetriesProduceOneAnyBarberBooking()
    {
        var ids = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            var service = new BookingService(new MongoBookingStore(connection, database), new TestClock(), new());
            return (await service.Create(Customer, Request with { BarberId = null }, idempotencyKey: "same-request-key-001")).Id;
        }));
        Assert.Single(ids.Distinct());
        Assert.Single(await Service.List(Customer, BookingTests.Start, BookingTests.Start.AddDays(1), null));
    }
    [MongoFact] public async Task SameKeyRacingDifferentBarbersCannotCreateTwoBookings()
    {
        async Task<int> Create(string barber)
        {
            var service = new BookingService(new MongoBookingStore(connection, database), new TestClock(), new());
            try { await service.Create(Customer, Request with { BarberId = barber }, idempotencyKey: "same-request-key-002"); return 201; }
            catch (DomainError e) { Assert.Equal("idempotency_conflict", e.Code); return e.Status; }
        }
        var statuses = await Task.WhenAll(Create("barber-a"), Create("barber-b"));
        Assert.Single(statuses, s => s == 201); Assert.Single(statuses, s => s == 409);
        Assert.Single(await Service.List(Customer, BookingTests.Start, BookingTests.Start.AddDays(1), null));
    }
    private static readonly Actor Admin = new("admin-a", "Admin");
    private static readonly WorkingPeriod[] Hours = Enumerable.Range(1, 6).Select(d => new WorkingPeriod(d, 540, 1020)).ToArray();
    [MongoFact] public async Task ConcurrentAdminsCannotLinkOneAccountToTwoBarbers()
    {
        async Task<int> Create(string name)
        {
            var catalogue = new CatalogueService(new MongoBookingStore(connection, database), new TestClock(), new());
            try { await catalogue.CreateBarber(Admin, new(name, "auth0|shared", Hours)); return 201; }
            catch (DomainError e) { Assert.Equal("duplicate_user", e.Code); return e.Status; }
        }
        var statuses = await Task.WhenAll(Create("First"), Create("Second"));
        Assert.Single(statuses, s => s == 201); Assert.Single(statuses, s => s == 409);
    }
    [MongoFact] public async Task DeactivationRacingABookingCannotBothSucceed()
    {
        async Task<int> Book()
        {
            try { await new BookingService(new MongoBookingStore(connection, database), new TestClock(), new()).Create(Customer, Request); return 201; }
            catch (DomainError e) { return e.Status; }
        }
        async Task<int> Deactivate()
        {
            try { await new CatalogueService(new MongoBookingStore(connection, database), new TestClock(), new()).SetBarberActive(Admin, "barber-a", false); return 200; }
            catch (DomainError e) { return e.Status; }
        }
        var outcomes = await Task.WhenAll(Book(), Deactivate());
        // Either the booking lands first and deactivation is refused, or deactivation wins and the booking finds no barber.
        Assert.True(outcomes is [201, 409] or [400, 200], $"Unexpected outcomes {string.Join(",", outcomes)}");
    }
    [MongoFact] public async Task CatalogueEditsPersistAndKeepScheduleRevisionIncreasing()
    {
        var catalogue = new CatalogueService(store, new TestClock(), new());
        var service = await catalogue.CreateService(Admin, new("Skin fade", 25000, 45));
        await catalogue.UpdateService(Admin, service.Id, new("Skin fade", 27500, 45));
        await catalogue.UpdateBarber(Admin, "barber-b", new("Renamed", "staff-b", Hours));
        var fresh = new MongoBookingStore(connection, database);
        Assert.Equal(27500, (await fresh.Read(s => s.Services())).Single(s => s.Id == service.Id).PriceCents);
        var barber = (await fresh.Read(s => s.Barbers())).Single(b => b.Id == "barber-b");
        Assert.Equal("Renamed", barber.Name); Assert.True(barber.Revision > 0);
    }
    [MongoFact] public async Task FreshConnectionCanReplayACommittedRequest()
    {
        var b = await Service.Create(Customer, Request, idempotencyKey: "same-request-key-003");
        var service = new BookingService(new MongoBookingStore(connection, database), new TestClock(), new());
        Assert.Equal(b.Id, (await service.Create(Customer, Request, idempotencyKey: "same-request-key-003")).Id);
    }
}
