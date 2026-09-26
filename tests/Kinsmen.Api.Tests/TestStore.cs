using Kinsmen.Api.Domain;
using Kinsmen.Api.Infrastructure;

namespace Kinsmen.Api.Tests;

// Test double only. The production application always uses MongoBookingStore.
public sealed class TestStore : IBookingStore, IBookingSession
{
    private readonly SemaphoreSlim gate = new(1);
    public List<ServiceItem> Catalog { get; } = [.. DemoData.Services];
    public List<Barber> Staff { get; } = [.. DemoData.Barbers];
    public List<Booking> Saved { get; private set; } = [];
    public List<TimeBlock> Blocked { get; private set; } = [];
    public List<Review> Reviews { get; private set; } = [];
    public async Task<T> Read<T>(Func<IBookingSession, Task<T>> action, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return await action(this); } finally { gate.Release(); }
    }
    public async Task<T> Write<T>(Func<IBookingSession, Task<T>> action, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        var saved = Saved.ToList(); var blocks = Blocked.ToList(); var catalog = Catalog.ToList(); var staff = Staff.ToList(); var reviews = Reviews.ToList();
        try { return await action(this); }
        catch
        {
            Saved = saved; Blocked = blocks; Reviews = reviews;
            Catalog.Clear(); Catalog.AddRange(catalog); Staff.Clear(); Staff.AddRange(staff);
            throw;
        }
        finally { gate.Release(); }
    }
    public Task<List<ServiceItem>> Services() => Task.FromResult(Catalog.ToList());
    public Task<List<Barber>> Barbers() => Task.FromResult(Staff.ToList());
    public Task TouchBarbers(IEnumerable<string> ids) => Task.CompletedTask;
    public Task<Booking?> BookingById(string id) => Task.FromResult(Saved.SingleOrDefault(b => b.Id == id));
    public Task<List<Booking>> Bookings(string? customerId, string? barberId, DateTime from, DateTime to)
        => Task.FromResult(Saved.Where(b => (customerId is null || b.CustomerId == customerId) && (barberId is null || b.BarberId == barberId)
            && b.StartUtc < to && b.EndUtc > from).ToList());
    public Task<List<TimeBlock>> Blocks(string barberId, DateTime from, DateTime to)
        => Task.FromResult(Blocked.Where(b => b.BarberId == barberId && b.StartUtc < to && b.EndUtc > from).ToList());
    public Task<TimeBlock?> BlockById(string id) => Task.FromResult(Blocked.SingleOrDefault(b => b.Id == id));
    public Task SaveBooking(Booking b, bool insert = false)
    {
        if (insert && Saved.Any(x => x.Id == b.Id)) throw new DuplicateBookingIdException();
        Saved.RemoveAll(x => x.Id == b.Id); Saved.Add(b); return Task.CompletedTask;
    }
    public Task SaveBlock(TimeBlock b) { Blocked.Add(b); return Task.CompletedTask; }
    public Task DeleteBlock(string id) { Blocked.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    public Task SaveService(ServiceItem service, bool insert = false)
    {
        var index = Catalog.FindIndex(x => x.Id == service.Id);
        if (insert) Catalog.Add(service);
        else if (index < 0) throw DomainError.Missing();
        else Catalog[index] = service;
        return Task.CompletedTask;
    }
    public Task SaveBarber(Barber barber, bool insert = false)
    {
        // Mirrors the unique barbers.userId index.
        if (Staff.Any(x => x.Id != barber.Id && x.UserId == barber.UserId)) throw new DuplicateBarberUserException();
        var index = Staff.FindIndex(x => x.Id == barber.Id);
        if (insert) Staff.Add(barber with { Revision = 0 });
        else if (index < 0) throw DomainError.Missing();
        else Staff[index] = barber with { Revision = Staff[index].Revision + 1 };
        return Task.CompletedTask;
    }
    public Task<Review?> ReviewByBookingId(string bookingId) => Task.FromResult(Reviews.SingleOrDefault(r => r.BookingId == bookingId));
    public Task<List<Review>> ReviewsForBarber(string barberId) => Task.FromResult(Reviews.Where(r => r.BarberId == barberId).ToList());
    public Task SaveReview(Review review)
    {
        // Mirrors the unique reviews.bookingId index.
        if (Reviews.Any(r => r.BookingId == review.BookingId)) throw new DuplicateReviewException();
        Reviews.Add(review);
        return Task.CompletedTask;
    }
}
public sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-09-18T06:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}
