namespace Kinsmen.Api.Domain;

// All writes run in a transaction. TouchBarbers must run before checking availability.
public interface IBookingStore
{
    Task<T> Read<T>(Func<IBookingSession, Task<T>> action, CancellationToken ct = default);
    Task<T> Write<T>(Func<IBookingSession, Task<T>> action, CancellationToken ct = default);
}

public interface IBookingSession
{
    Task<List<ServiceItem>> Services();
    Task<List<Barber>> Barbers();
    Task TouchBarbers(IEnumerable<string> ids);
    Task<Booking?> BookingById(string id);
    Task<List<Booking>> Bookings(string? customerId, string? barberId, DateTime from, DateTime to);
    Task<List<TimeBlock>> Blocks(string barberId, DateTime from, DateTime to);
    Task<TimeBlock?> BlockById(string id);
    Task SaveBooking(Booking booking, bool insert = false);
    Task SaveBlock(TimeBlock block);
    Task DeleteBlock(string id);
}
